using System;
using System.Linq;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Authentication;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unslop.UnityBridge.Editor.UI
{
    public sealed class UnslopBridgeWindow : EditorWindow
    {
        ProjectBindingService _binding;
        IUnslopApiClient _api;
        Label _statusLabel;
        TextField _apiKeyField;
        ListView _projectList;
        ListView _assetList;
        ListView _versionList;
        ListView _installedList;
        Label _detailLabel;
        ProjectDto[] _projects = Array.Empty<ProjectDto>();
        AssetSummaryDto[] _assets = Array.Empty<AssetSummaryDto>();
        AssetVersionSummaryDto[] _versions = Array.Empty<AssetVersionSummaryDto>();

        [MenuItem(PackageInfo.MenuRoot + "/Asset Bridge")]
        public static void Open()
        {
            var window = GetWindow<UnslopBridgeWindow>();
            window.titleContent = new GUIContent(PackageInfo.WindowTitle);
            window.minSize = new Vector2(720, 480);
        }

        void OnEnable()
        {
            _binding = new ProjectBindingService();
            RefreshApi();
        }

        void CreateGUI()
        {
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            _statusLabel = new Label();
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.marginBottom = 8;
            rootVisualElement.Add(_statusLabel);

            var tabs = new TabView();
            tabs.Add(BuildConnectTab());
            tabs.Add(BuildBrowseTab());
            tabs.Add(BuildInstalledTab());
            rootVisualElement.Add(tabs);
            RefreshStatus();
            RefreshInstalled();
        }

        Tab BuildConnectTab()
        {
            var tab = new Tab("Connect");
            var col = new VisualElement();
            col.style.flexDirection = FlexDirection.Column;

            col.Add(new Label("Bridge API key (stored under Library/Unslop/Auth, never in project files):"));
            _apiKeyField = new TextField("API Key") { isPasswordField = true };
            col.Add(_apiKeyField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            var saveBtn = new Button(() =>
            {
                try
                {
                    _binding.SaveApiKey(_apiKeyField.value);
                    _apiKeyField.value = string.Empty;
                    RefreshApi();
                    RefreshStatus();
                    BridgeLog.Info("Bridge API key saved.");
                }
                catch (Exception ex)
                {
                    BridgeLog.Exception(ex, "Save API key");
                    EditorUtility.DisplayDialog("Unslop", ex.Message, "OK");
                }
            }) { text = "Save Key" };
            var clearBtn = new Button(() =>
            {
                _binding.SignOut();
                RefreshStatus();
                RefreshInstalled();
            }) { text = "Sign Out" };
            row.Add(saveBtn);
            row.Add(clearBtn);
            col.Add(row);

            var refreshProjects = new Button(() => _ = LoadProjectsAsync()) { text = "Refresh Projects" };
            refreshProjects.style.marginTop = 10;
            col.Add(refreshProjects);

            _projectList = new ListView
            {
                fixedItemHeight = 22,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var p = _projects[i];
                    ((Label)el).text = $"{p.name}  ({p.role})  {p.project_id}";
                }
            };
            _projectList.selectedIndicesChanged += _ =>
            {
                var idx = _projectList.selectedIndex;
                if (idx < 0 || idx >= _projects.Length)
                {
                    return;
                }

                _binding.BindProject(_projects[idx]);
                RefreshStatus();
                _ = LoadAssetsAsync();
            };
            _projectList.style.flexGrow = 1;
            _projectList.style.minHeight = 160;
            col.Add(_projectList);
            tab.Add(col);
            return tab;
        }

        Tab BuildBrowseTab()
        {
            var tab = new Tab("Browse");
            var root = new VisualElement { style = { flexDirection = FlexDirection.Column, flexGrow = 1 } };
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(new Button(() => _ = LoadAssetsAsync()) { text = "Refresh Assets" });
            buttons.Add(new Button(() => _ = LoadVersionsForSelectionAsync()) { text = "Refresh Versions" });
            root.Add(buttons);

            var split = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, marginTop = 6 } };
            _assetList = new ListView
            {
                fixedItemHeight = 28,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var a = _assets[i];
                    var rec = string.IsNullOrEmpty(a.recommended_version_id) ? "no published version" : a.recommended_version_id;
                    ((Label)el).text = $"{a.display_name}\n{a.lifecycle} | api={a.api_available} | rec={rec}";
                }
            };
            _assetList.selectedIndicesChanged += _ => _ = LoadVersionsForSelectionAsync();
            _assetList.style.width = Length.Percent(50);
            _assetList.style.flexGrow = 1;

            var right = new VisualElement { style = { flexGrow = 1, width = Length.Percent(50), marginLeft = 8 } };
            _versionList = new ListView
            {
                fixedItemHeight = 22,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var v = _versions[i];
                    ((Label)el).text = $"v{v.version_number} {v.state} {v.asset_version_id}";
                }
            };
            _versionList.selectedIndicesChanged += _ => UpdateDetail();
            _detailLabel = new Label("Select an asset/version.") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            right.Add(_versionList);
            right.Add(_detailLabel);

            split.Add(_assetList);
            split.Add(right);
            root.Add(split);
            tab.Add(root);
            return tab;
        }

        Tab BuildInstalledTab()
        {
            var tab = new Tab("Installed");
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column, flexGrow = 1 } };
            col.Add(new Button(RefreshInstalled) { text = "Refresh Lock File" });
            _installedList = new ListView
            {
                fixedItemHeight = 40,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var items = _installedList.itemsSource as System.Collections.Generic.List<string>;
                    ((Label)el).text = items != null && i < items.Count ? items[i] : string.Empty;
                }
            };
            _installedList.style.flexGrow = 1;
            _installedList.style.minHeight = 240;
            col.Add(_installedList);
            tab.Add(col);
            return tab;
        }

        void RefreshApi()
        {
            _api = BridgeServices.CreateApiClient();
        }

        void RefreshStatus()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var auth = _binding.IsAuthenticated ? "authenticated" : "signed out";
            var project = string.IsNullOrEmpty(_binding.BoundProjectId)
                ? "no project bound"
                : $"{_binding.BoundProjectName} ({_binding.BoundProjectId})";
            var corr = _api?.LastCorrelationId;
            _statusLabel.text = $"Status: {auth} | Project: {project} | Correlation: {corr ?? "-"} | API: {BridgeServices.Settings.ApiBaseUrl}";
        }

        void RefreshInstalled()
        {
            if (_installedList == null)
            {
                return;
            }

            var settings = BridgeServices.Settings;
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            var lines = lockFile.assets.Select(kv =>
                $"{kv.Key}\nversion={kv.Value.installed_version_id}  physical={kv.Value.physical_spec_id}  wrapper={kv.Value.wrapper_prefab_guid}").ToList();
            if (lines.Count == 0)
            {
                lines.Add("No installed assets in Unslop.lock.json yet.");
            }

            _installedList.itemsSource = lines;
            _installedList.RefreshItems();
        }

        async Task LoadProjectsAsync()
        {
            try
            {
                RefreshApi();
                var page = await _binding.ListProjectsAsync();
                _projects = page?.data?.ToArray() ?? Array.Empty<ProjectDto>();
                _projectList.itemsSource = _projects;
                _projectList.RefreshItems();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List projects");
                EditorUtility.DisplayDialog("Unslop", BridgeLog.Redact(ex.Message), "OK");
                RefreshStatus();
            }
        }

        async Task LoadAssetsAsync()
        {
            try
            {
                var projectId = _binding.BoundProjectId;
                if (string.IsNullOrEmpty(projectId))
                {
                    EditorUtility.DisplayDialog("Unslop", "Bind a project first.", "OK");
                    return;
                }

                RefreshApi();
                var page = await _api.ListProjectAssetsAsync(projectId);
                _assets = page?.data?.ToArray() ?? Array.Empty<AssetSummaryDto>();
                _assetList.itemsSource = _assets;
                _assetList.RefreshItems();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List assets");
                EditorUtility.DisplayDialog("Unslop", BridgeLog.Redact(ex.Message), "OK");
                RefreshStatus();
            }
        }

        async Task LoadVersionsForSelectionAsync()
        {
            try
            {
                var idx = _assetList.selectedIndex;
                if (idx < 0 || idx >= _assets.Length)
                {
                    return;
                }

                RefreshApi();
                var asset = _assets[idx];
                var page = await _api.ListAssetVersionsAsync(asset.asset_id);
                _versions = page?.data?.ToArray() ?? Array.Empty<AssetVersionSummaryDto>();
                _versionList.itemsSource = _versions;
                _versionList.RefreshItems();
                if (_versions.Length == 0)
                {
                    _detailLabel.text = $"{asset.display_name}\nNo published versions.";
                }
                else
                {
                    UpdateDetail();
                }

                RefreshStatus();
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List versions");
                RefreshStatus();
            }
        }

        void UpdateDetail()
        {
            var aIdx = _assetList.selectedIndex;
            var vIdx = _versionList.selectedIndex;
            if (aIdx < 0 || aIdx >= _assets.Length)
            {
                return;
            }

            var asset = _assets[aIdx];
            if (vIdx < 0 || vIdx >= _versions.Length)
            {
                _detailLabel.text = $"{asset.display_name}\nrecommended={asset.recommended_version_id ?? "null"}\nphysical_spec={asset.current_physical_spec_id ?? "null"}";
                return;
            }

            var v = _versions[vIdx];
            _detailLabel.text = $"{asset.display_name}\nv{v.version_number} {v.state}\n{v.asset_version_id}\nmanifest={v.manifest_sha256}\npipeline={v.pipeline_origin}\npublished={v.published_at}";
        }
    }
}
