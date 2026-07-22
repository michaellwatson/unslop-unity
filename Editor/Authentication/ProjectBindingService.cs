using System;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;
using UnityEditor;

namespace Unslop.UnityBridge.Editor.Authentication
{
    public sealed class ProjectBindingService
    {
        readonly BridgeCredentialStore _credentials;
        readonly Func<IUnslopApiClient> _clientFactory;

        public ProjectBindingService(BridgeCredentialStore credentials = null, Func<IUnslopApiClient> clientFactory = null)
        {
            _credentials = credentials ?? BridgeServices.CredentialStore;
            _clientFactory = clientFactory ?? BridgeServices.CreateApiClient;
        }

        public bool IsAuthenticated => _credentials.HasApiKey;

        /// <summary>
        /// True when the last catalogue call failed with 401/403. Binding and installed assets are left intact;
        /// the user can refresh the key without losing project state.
        /// </summary>
        public bool NeedsReauthentication { get; private set; }

        public string LastError { get; private set; }
        public string LastCorrelationId { get; private set; }

        public string BoundProjectId => BridgeServices.Settings.BoundProjectId;
        public string BoundProjectName => BridgeServices.Settings.BoundProjectName;

        public void SaveApiKey(string apiKey)
        {
            NeedsReauthentication = false;
            LastError = null;
            _credentials.SaveApiKey(apiKey);
        }

        public void Logout()
        {
            _credentials.Clear();
            var settings = BridgeServices.Settings;
            settings.BoundProjectId = string.Empty;
            settings.BoundProjectName = string.Empty;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            NeedsReauthentication = false;
            LastError = null;
            BridgeLog.Info("Logged out; API key cleared and project binding removed. Installed assets left unchanged.");
        }

        /// <summary>Alias for Logout.</summary>
        public void SignOut() => Logout();

        public async Task<CursorPage<ProjectDto>> ListProjectsAsync(
            string cursor = null,
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            NeedsReauthentication = false;
            LastError = null;
            try
            {
                var client = _clientFactory();
                var page = await client.ListProjectsAsync(cursor, limit, cancellationToken).ConfigureAwait(true);
                LastCorrelationId = client.LastCorrelationId;
                return page ?? new CursorPage<ProjectDto>();
            }
            catch (UnslopApiException ex) when (ex.IsUnauthorized)
            {
                NeedsReauthentication = true;
                LastCorrelationId = ex.CorrelationId;
                LastError = "Authorization failed or was revoked. Update or clear the Bridge API key.";
                BridgeLog.Warn($"Auth rejected ({ex.StatusCode}) correlation={ex.CorrelationId}. Recoverable — re-enter key.");
                return new CursorPage<ProjectDto>();
            }
        }

        public void BindProject(ProjectDto project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.project_id))
            {
                throw new ArgumentException("Project is required.", nameof(project));
            }

            NeedsReauthentication = false;
            LastError = null;

            var settings = UnslopProjectSettings.EnsureExists();
            settings.BoundProjectId = project.project_id;
            settings.BoundProjectName = project.name ?? string.Empty;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            var lockFile = LockFileService.LoadOrCreate(project.project_id, settings.Environment);
            lockFile.project_id = project.project_id;
            lockFile.environment = settings.Environment;
            LockFileService.Save(lockFile);
            BridgeLog.Info($"Bound project {project.name} ({project.project_id}).");
        }
    }
}
