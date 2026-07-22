using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Authentication;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Settings;

namespace Unslop.UnityBridge.Editor.Services
{
    /// <summary>
    /// Composition root for Editor services: credentials, project settings, and API client.
    /// </summary>
    public static class BridgeServices
    {
        static readonly BridgeCredentialStore Credentials = new BridgeCredentialStore();

        public static BridgeCredentialStore CredentialStore => Credentials;

        public static UnslopProjectSettings Settings => UnslopProjectSettings.EnsureExists();

        public static IUnslopApiClient CreateApiClient()
        {
            var settings = Settings;
            var baseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
                ? PackageInfo.DefaultApiBaseUrl
                : settings.ApiBaseUrl;
            return new UnslopApiClient(baseUrl, () => Credentials.LoadApiKey());
        }

        public static ProjectBindingService CreateBindingService()
            => new ProjectBindingService(Credentials, CreateApiClient);

        public static ClientContextDto CreateClientContext()
        {
            return new ClientContextDto
            {
                engine = "unity",
                unity_version = UnityEngine.Application.unityVersion,
                bridge_version = PackageInfo.Version,
                render_pipeline = "urp",
                manifest_schema_versions = new System.Collections.Generic.List<int> { 1 }
            };
        }
    }
}
