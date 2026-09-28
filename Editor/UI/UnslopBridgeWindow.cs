using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unslop.UnityBridge;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Authentication;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Browser;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Install;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Publishing;
using Unslop.UnityBridge.Editor.Scale;
using Unslop.UnityBridge.Editor.Services;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unslop.UnityBridge.Editor.UI
{
    public sealed class UnslopBridgeWindow : EditorWindow
    {
        ProjectBindingService _binding;
        IUnslopApiClient _api;
        Label _authStatus;
        Label _projectStatus;
        Label _correlationStatus;
        Label _messageLabel;
        TextField _apiKeyField;
        TextField _apiBaseField;
        ListView _projectList;
        ListView _assetList;
        ListView _versionList;
        ListView _installedList;
        Label _detailLabel;
        ObjectField _publishPrefabField;
        TextField _publishDisplayNameField;
        Toggle _publishNewAssetToggle;
        DropdownField _publishExistingAssetField;
        Button _publishButton;

        ProjectDto[] _projects = Array.Empty<ProjectDto>();
        AssetSummaryDto[] _assets = Array.Empty<AssetSummaryDto>();
        AssetVersionSummaryDto[] _versions = Array.Empty<AssetVersionSummaryDto>();
        bool _busy;

        [MenuItem(BridgePackageInfo.MenuRoot + "/Asset Bridge")]
        public static void Open()
        {
            var window = GetWindow<UnslopBridgeWindow>();
            window.titleContent = new GUIContent(BridgePackageInfo.WindowTitle);
            window.minSize = new Vector2(720, 480);
        }

        void OnEnable()
        {
            _binding = BridgeServices.CreateBindingService();
            RefreshApi();
        }

        void CreateGUI()
        {
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            rootVisualElement.Add(BuildStatusBar());
            _messageLabel = new Label(string.Empty);
            _messageLabel.style.whiteSpace = WhiteSpace.Normal;
            _messageLabel.style.marginBottom = 8;
            rootVisualElement.Add(_messageLabel);

            var tabs = new TabView();
            tabs.Add(BuildConnectTab());
            tabs.Add(BuildBrowseTab());
            tabs.Add(BuildInstalledTab());
            if (FeatureFlagService.IsEnabled("unity_bridge_publish_from_prefab"))
            {
                tabs.Add(BuildPublishTab());
            }

            rootVisualElement.Add(tabs);

            RefreshStatus();
            RefreshInstalled();
            TrySeedPublishFromSelection();
        }

        VisualElement BuildStatusBar()
        {
            var bar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    marginBottom = 6,
                    paddingBottom = 6,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0.25f, 0.25f, 0.25f)
                }
            };
            _authStatus = new Label();
            _projectStatus = new Label();
            _correlationStatus = new Label();
            bar.Add(_authStatus);
            bar.Add(_projectStatus);
            bar.Add(_correlationStatus);
            return bar;
        }

        Tab BuildConnectTab()
        {
            var tab = new Tab("Connect");
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column } };

            _apiBaseField = new TextField("API Base URL")
            {
                value = BridgeServices.Settings.ApiBaseUrl ?? BridgePackageInfo.DefaultApiBaseUrl
            };
            col.Add(_apiBaseField);

            col.Add(new Label("Bridge API key (Library/Unslop/Auth only — never logged or committed):"));
            _apiKeyField = new TextField("API Key") { isPasswordField = true };
            col.Add(_apiKeyField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
            row.Add(new Button(OnSaveKey) { text = "Save Key" });
            row.Add(new Button(OnLogout) { text = "Clear / Logout" });
            row.Add(new Button(() => _ = TestConnectionAsync()) { text = "Test Connection" });
            col.Add(row);

            var help = new Label(
                $"Paste usk_… then Test Connection (saves the key automatically). Default API: {BridgePackageInfo.DefaultApiBaseUrl}");
            help.style.marginTop = 10;
            help.style.opacity = 0.75f;
            help.style.whiteSpace = WhiteSpace.Normal;
            col.Add(help);

            var debugBox = new VisualElement { style = { marginTop = 12 } };
            debugBox.Add(new Label("Diagnostics") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            var debugToggle = new Toggle("Debug logging (project upload)")
            {
                value = BridgeDebugMode.Enabled,
                tooltip = "Logs bridge activity locally and uploads to the bound project via " +
                          "POST /projects/{id}/diagnostics (Meshy vs artist scale/rotation diffs included)."
            };
            debugToggle.RegisterValueChangedCallback(evt =>
            {
                BridgeDebugMode.Enabled = evt.newValue;
                SetMessage(evt.newValue
                    ? "Debug logging ON — local file + project-scoped API upload available."
                    : "Debug logging OFF.");
            });
            debugBox.Add(debugToggle);
            var debugRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            debugRow.Add(new Button(BridgeDebugMode.RevealLog) { text = "Reveal Debug Log" });
            debugRow.Add(new Button(() =>
            {
                BridgeDebugMode.ClearLog();
                SetMessage("Debug log cleared (new session).");
            }) { text = "Clear Debug Log" });
            debugRow.Add(new Button(() => _ = UploadDebugLogAsync()) { text = "Upload Debug Log" });
            debugRow.Add(new Button(() => _ = PullDebugLogAsync()) { text = "Pull Latest Debug Log" });
            debugRow.Add(new Button(() =>
            {
                try
                {
                    var path = SupportExport.ExportRedactedDiagnostics("manual");
                    SetMessage("Support export: " + path);
                }
                catch (Exception ex)
                {
                    BridgeLog.Exception(ex, "Support export");
                    SetMessage(ex.Message);
                }
            }) { text = "Export Support Zip" });
            debugBox.Add(debugRow);
            col.Add(debugBox);

            col.Add(new Label("Projects (select to bind):") { style = { marginTop = 12, unityFontStyleAndWeight = FontStyle.Bold } });
            _projectList = new ListView
            {
                fixedItemHeight = 22,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var p = _projects[i];
                    ((Label)el).text = $"{p.name}  ({p.role})  {p.project_id}";
                }
            };
            _projectList.selectedIndicesChanged += selection =>
            {
                var idx = _projectList.selectedIndex;
                if (idx < 0 || idx >= _projects.Length)
                {
                    return;
                }

                try
                {
                    _binding.BindProject(_projects[idx]);
                    SetMessage($"Bound '{_projects[idx].name}'.");
                    RefreshStatus();
                    _ = LoadAssetsAsync();
                }
                catch (Exception ex)
                {
                    BridgeLog.Exception(ex, "Bind project");
                    SetMessage(ex.Message);
                }
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
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            buttons.Add(new Button(() => _ = LoadProjectsAsync()) { text = "Refresh Projects" });
            buttons.Add(new Button(() => _ = LoadAssetsAsync()) { text = "Refresh Assets" });
            buttons.Add(new Button(() => _ = LoadVersionsForSelectionAsync()) { text = "Refresh Versions" });
            buttons.Add(new Button(() => _ = InstallSelectedAsync()) { text = "Install Selected Version" });
            buttons.Add(new Button(() => _ = CheckUpdatesAsync()) { text = "Check Updates" });
            buttons.Add(new Button(() => _ = AcceptCurrentScaleAsync()) { text = "Accept Current Scale" });
            buttons.Add(new Button(() => _ = SetCanonicalScaleAsync()) { text = "Set Canonical Scale" });
            buttons.Add(new Button(() => _ = ConfirmScaleAsync()) { text = "Confirm Scale" });
            root.Add(buttons);

            var split = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, marginTop = 6 } };
            _assetList = new ListView
            {
                fixedItemHeight = 28,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var a = _assets[i];
                    var rec = string.IsNullOrEmpty(a.recommended_version_id) ? "no recommended" : a.recommended_version_id;
                    ((Label)el).text = $"{a.display_name}\n{a.lifecycle} | api={a.api_available} | rec={rec}";
                }
            };
            _assetList.selectedIndicesChanged += selection => _ = LoadVersionsForSelectionAsync();
            _assetList.style.width = Length.Percent(50);
            _assetList.style.flexGrow = 1;

            var right = new VisualElement { style = { flexGrow = 1, width = Length.Percent(50), marginLeft = 8 } };
            _versionList = new ListView
            {
                fixedItemHeight = 22,
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (el, i) =>
                {
                    var v = _versions[i];
                    ((Label)el).text = $"v{v.version_number} {v.state} {v.asset_version_id}";
                }
            };
            _versionList.selectedIndicesChanged += _ => UpdateDetail();
            _detailLabel = new Label("Select an asset/version.") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            var actionCol = new VisualElement { style = { flexDirection = FlexDirection.Column, marginTop = 8 } };
            actionCol.Add(new Button(() => _ = InstallSelectedAsync()) { text = "Install Selected Version" });
            actionCol.Add(new Button(() => _ = CheckUpdatesAsync()) { text = "Check Updates" });
            actionCol.Add(new Button(() => _ = AcceptCurrentScaleAsync()) { text = "Accept Current Scale" });
            actionCol.Add(new Button(() => _ = SetCanonicalScaleAsync()) { text = "Set Canonical Scale" });
            actionCol.Add(new Button(() => _ = ConfirmScaleAsync()) { text = "Confirm Scale" });
            right.Add(_versionList);
            right.Add(_detailLabel);
            right.Add(actionCol);

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
                    var items = _installedList.itemsSource as List<string>;
                    ((Label)el).text = items != null && i < items.Count ? items[i] : string.Empty;
                }
            };
            _installedList.style.flexGrow = 1;
            _installedList.style.minHeight = 240;
            col.Add(_installedList);
            tab.Add(col);
            return tab;
        }

        Tab BuildPublishTab()
        {
            var tab = new Tab("Publish");
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column, flexGrow = 1 } };

            var help = new Label(
                "Select a Project prefab (or an Unslop wrapper). The bridge exports a neutral FBX package " +
                "and publishes it as a catalog asset version. Install Package Manager → FBX Exporter if the " +
                "mesh is not already an .fbx asset.");
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.opacity = 0.8f;
            help.style.marginBottom = 8;
            col.Add(help);

            _publishPrefabField = new ObjectField("Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true
            };
            _publishPrefabField.RegisterValueChangedCallback(evt =>
            {
                var go = evt.newValue as GameObject;
                if (go != null && string.IsNullOrWhiteSpace(_publishDisplayNameField?.value))
                {
                    _publishDisplayNameField.value = go.name;
                }

                PrefillExistingAssetFromPrefab(go);
            });
            col.Add(_publishPrefabField);

            var selectionRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6 } };
            selectionRow.Add(new Button(TrySeedPublishFromSelection) { text = "Use Current Selection" });
            col.Add(selectionRow);

            _publishDisplayNameField = new TextField("Display Name");
            col.Add(_publishDisplayNameField);

            _publishNewAssetToggle = new Toggle("Create new catalog asset") { value = true };
            _publishNewAssetToggle.RegisterValueChangedCallback(_ => UpdatePublishTargetUi());
            col.Add(_publishNewAssetToggle);

            _publishExistingAssetField = new DropdownField("Existing asset")
            {
                choices = new List<string> { "(refresh assets on Browse first)" }
            };
            col.Add(_publishExistingAssetField);

            _publishButton = new Button(() => _ = PublishPrefabAsync()) { text = "Publish Prefab" };
            _publishButton.style.marginTop = 10;
            _publishButton.style.height = 28;
            col.Add(_publishButton);

            UpdatePublishTargetUi();
            RefreshPublishAssetChoices();
            tab.Add(col);
            return tab;
        }

        void UpdatePublishTargetUi()
        {
            if (_publishExistingAssetField == null || _publishNewAssetToggle == null)
            {
                return;
            }

            _publishExistingAssetField.SetEnabled(!_publishNewAssetToggle.value);
        }

        void RefreshPublishAssetChoices()
        {
            if (_publishExistingAssetField == null)
            {
                return;
            }

            if (_assets == null || _assets.Length == 0)
            {
                _publishExistingAssetField.choices = new List<string> { "(no assets — Refresh Assets on Browse)" };
                _publishExistingAssetField.index = 0;
                return;
            }

            _publishExistingAssetField.choices = _assets
                .Select(a => $"{a.display_name}  ({ShortId(a.asset_id)})")
                .ToList();
            if (_publishExistingAssetField.index < 0 || _publishExistingAssetField.index >= _assets.Length)
            {
                _publishExistingAssetField.index = 0;
            }
        }

        void TrySeedPublishFromSelection()
        {
            if (_publishPrefabField == null)
            {
                return;
            }

            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                _publishPrefabField.value = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(selected)
                     || selected.GetComponentInChildren<UnslopAssetReference>(true) != null)
            {
                _publishPrefabField.value = selected;
            }
            else
            {
                return;
            }

            if (_publishDisplayNameField != null && string.IsNullOrWhiteSpace(_publishDisplayNameField.value))
            {
                _publishDisplayNameField.value = selected.name;
            }

            PrefillExistingAssetFromPrefab(_publishPrefabField.value as GameObject);
        }

        void PrefillExistingAssetFromPrefab(GameObject go)
        {
            if (go == null || _assets == null || _assets.Length == 0)
            {
                return;
            }

            PrefabPackageExporter.ResolveExportRoot(go, out var linkedAssetId);
            if (string.IsNullOrWhiteSpace(linkedAssetId))
            {
                var reference = go.GetComponentInChildren<UnslopAssetReference>(true);
                linkedAssetId = reference?.AssetId;
            }

            if (string.IsNullOrWhiteSpace(linkedAssetId))
            {
                return;
            }

            var idx = Array.FindIndex(_assets, a => string.Equals(a.asset_id, linkedAssetId, StringComparison.Ordinal));
            if (idx < 0)
            {
                return;
            }

            if (_publishNewAssetToggle != null)
            {
                _publishNewAssetToggle.value = false;
            }

            RefreshPublishAssetChoices();
            if (_publishExistingAssetField != null)
            {
                _publishExistingAssetField.index = idx;
            }

            UpdatePublishTargetUi();
        }

        async Task PublishPrefabAsync()
        {
            if (!FeatureFlagService.IsEnabled("unity_bridge_publish_from_prefab"))
            {
                SetMessage("Publish-from-prefab is disabled (Unslop.Feature.unity_bridge_publish_from_prefab).");
                return;
            }

            if (!BeginBusy("Publishing…"))
            {
                return;
            }

            try
            {
                var prefab = _publishPrefabField?.value as GameObject;
                if (prefab == null)
                {
                    SetMessage("Select a prefab (or scene instance) to publish.");
                    return;
                }

                var projectId = _binding.BoundProjectId;
                if (string.IsNullOrEmpty(projectId))
                {
                    SetMessage("Bind a project first (Connect tab).");
                    return;
                }

                if (!_binding.IsAuthenticated)
                {
                    SetMessage("Paste a Bridge API key on Connect, then Test Connection.");
                    return;
                }

                string existingAssetId = null;
                if (_publishNewAssetToggle != null && !_publishNewAssetToggle.value)
                {
                    var idx = _publishExistingAssetField?.index ?? -1;
                    if (idx < 0 || idx >= _assets.Length)
                    {
                        SetMessage("Select an existing asset, or enable Create new catalog asset.");
                        return;
                    }

                    existingAssetId = _assets[idx].asset_id;
                }

                var displayName = string.IsNullOrWhiteSpace(_publishDisplayNameField?.value)
                    ? prefab.name
                    : _publishDisplayNameField.value.Trim();

                if (!EditorUtility.DisplayDialog(
                        "Unslop",
                        string.IsNullOrEmpty(existingAssetId)
                            ? $"Create catalog asset '{displayName}' and publish a version from this prefab?"
                            : $"Publish a new version of '{displayName}' from this prefab?",
                        "Publish",
                        "Cancel"))
                {
                    return;
                }

                PersistApiBaseFromField();
                RefreshApi();
                var progress = new Progress<string>(SetMessage);
                var result = await new PrefabPackagePublisher(_api).PublishAsync(
                    new PrefabPublishRequest
                    {
                        Prefab = prefab,
                        DisplayName = displayName,
                        ProjectId = projectId,
                        ExistingAssetId = existingAssetId,
                        Recommend = true
                    },
                    progress).ConfigureAwait(true);

                await ContinueOnMainThread(() =>
                {
                    SetMessage(
                        $"Published '{result.DisplayName}' as asset {ShortId(result.AssetId)} " +
                        $"version {ShortId(result.AssetVersionId)} (v{result.VersionNumber}).");
                    RefreshStatus();
                });

                // Refresh catalogue without nesting BeginBusy.
                try
                {
                    var page = await _api.ListProjectAssetsAsync(projectId).ConfigureAwait(true);
                    await ContinueOnMainThread(() =>
                    {
                        _assets = page?.data?.ToArray() ?? Array.Empty<AssetSummaryDto>();
                        if (_assetList != null)
                        {
                            _assetList.itemsSource = _assets;
                            _assetList.RefreshItems();
                        }

                        RefreshPublishAssetChoices();
                    });
                }
                catch (Exception refreshEx)
                {
                    BridgeLog.Warn("Post-publish asset refresh failed: " + BridgeLog.Redact(refreshEx.Message));
                }

                var installProgress = new Progress<string>(SetMessage);
                var installResult = await new AssetInstallService(_api)
                    .InstallAsync(result.AssetId, result.AssetVersionId, installProgress)
                    .ConfigureAwait(true);

                await ContinueOnMainThread(() =>
                {
                    var refreshedPrefab = PublishedPrefabRefresher.RefreshAfterInstall(prefab, installResult);
                    if (_publishPrefabField != null && refreshedPrefab != null)
                    {
                        _publishPrefabField.value = refreshedPrefab;
                    }

                    if (refreshedPrefab != null)
                    {
                        Selection.activeObject = refreshedPrefab;
                        EditorGUIUtility.PingObject(refreshedPrefab);
                    }

                    RefreshInstalled();
                    SetMessage(
                        $"Published and updated local prefab to v{result.VersionNumber} " +
                        $"({ShortId(result.AssetVersionId)}).");
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Publish prefab");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        void OnSaveKey()
        {
            try
            {
                if (!TryPersistKeyFromField(requireFieldValue: true, out var error))
                {
                    SetMessage(error);
                    return;
                }

                _apiKeyField.value = string.Empty;
                RefreshApi();
                RefreshStatus();
                SetMessage("API key saved to local Library store. Click Test Connection to list projects.");
                BridgeLog.Info("Bridge API key saved.");
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Save API key");
                SetMessage(ex.Message);
            }
        }

        /// <summary>
        /// Persist API base + optional key typed in the field. Test Connection must save first —
        /// the API client only reads the Library store, not the TextField.
        /// </summary>
        bool TryPersistKeyFromField(bool requireFieldValue, out string error)
        {
            error = null;
            PersistApiBaseFromField();
            var key = _apiKeyField?.value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                if (requireFieldValue)
                {
                    error = "Enter an API key starting with usk_.";
                    return false;
                }

                return true;
            }

            _binding.SaveApiKey(key);
            return true;
        }

        async Task UploadDebugLogAsync()
        {
            try
            {
                PersistApiBaseFromField();
                RefreshApi();
                var projectId = BridgeServices.Settings.BoundProjectId;
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    SetMessage("Bind a project before uploading the debug log.");
                    return;
                }

                if (!BridgeDebugMode.Enabled && !System.IO.File.Exists(BridgeDebugMode.LogFilePath))
                {
                    SetMessage("No debug log to upload. Enable Debug logging first.");
                    return;
                }

                SetMessage("Uploading debug log to project…");
                var result = await BridgeDebugUploader.FlushAsync(_api, projectId, isFinal: true).ConfigureAwait(true);
                await ContinueOnMainThread(() =>
                {
                    SetMessage(result == null
                        ? "No new debug log content to upload."
                        : $"Uploaded debug session {ShortId(result.session_id)} ({result.byte_length} bytes) to project.");
                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Upload debug log");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
        }

        async Task PullDebugLogAsync()
        {
            try
            {
                PersistApiBaseFromField();
                RefreshApi();
                var projectId = BridgeServices.Settings.BoundProjectId;
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    SetMessage("Bind a project before pulling debug logs.");
                    return;
                }

                SetMessage("Pulling latest project debug log…");
                var path = await BridgeDebugUploader.PullLatestAsync(_api, projectId).ConfigureAwait(true);
                await ContinueOnMainThread(() =>
                {
                    SetMessage("Pulled debug log: " + path);
                    EditorUtility.RevealInFinder(path);
                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Pull debug log");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
        }

        async Task TestConnectionAsync()
        {
            try
            {
                // Always take the field value if present — users expect "paste then test" to work.
                if (!TryPersistKeyFromField(requireFieldValue: false, out var persistError))
                {
                    SetMessage(persistError);
                    return;
                }

                if (!_binding.IsAuthenticated)
                {
                    SetMessage("Paste a Bridge API key starting with usk_, then click Test Connection.");
                    RefreshStatus();
                    return;
                }

                // Clear the field only after we know a key is stored (security).
                if (!string.IsNullOrEmpty(_apiKeyField?.value))
                {
                    _apiKeyField.value = string.Empty;
                }

                await LoadProjectsAsync();
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Test connection");
                SetMessage(ex.Message);
            }
        }

        void OnLogout()
        {
            _binding.Logout();
            _apiKeyField.value = string.Empty;
            _projects = Array.Empty<ProjectDto>();
            _assets = Array.Empty<AssetSummaryDto>();
            _versions = Array.Empty<AssetVersionSummaryDto>();
            if (_projectList != null)
            {
                _projectList.itemsSource = _projects;
                _projectList.RefreshItems();
            }

            if (_assetList != null)
            {
                _assetList.itemsSource = _assets;
                _assetList.RefreshItems();
            }

            if (_versionList != null)
            {
                _versionList.itemsSource = _versions;
                _versionList.RefreshItems();
            }

            RefreshStatus();
            RefreshInstalled();
            SetMessage("Logged out. API key cleared.");
        }

        void PersistApiBaseFromField()
        {
            var settings = BridgeServices.Settings;
            var url = string.IsNullOrWhiteSpace(_apiBaseField?.value)
                ? BridgePackageInfo.DefaultApiBaseUrl
                : _apiBaseField.value.TrimEnd('/');
            settings.ApiBaseUrl = url;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        void RefreshApi()
        {
            _api = BridgeServices.CreateApiClient();
        }

        void RefreshStatus()
        {
            if (_authStatus == null)
            {
                return;
            }

            var auth = !_binding.IsAuthenticated
                ? "Auth: signed out"
                : _binding.NeedsReauthentication
                    ? "Auth: key rejected (recoverable)"
                    : "Auth: key present";
            _authStatus.text = auth;
            _projectStatus.text = string.IsNullOrEmpty(_binding.BoundProjectId)
                ? "Project: (none)"
                : $"Project: {_binding.BoundProjectName} ({_binding.BoundProjectId})";
            var corr = _api?.LastCorrelationId ?? _binding.LastCorrelationId;
            _correlationStatus.text = string.IsNullOrEmpty(corr) ? "Correlation: —" : $"Correlation: {corr}";
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
            {
                var name = string.IsNullOrWhiteSpace(kv.Value.display_name) ? kv.Key : kv.Value.display_name;
                return $"{name}\nid={ShortId(kv.Key)}  version={ShortId(kv.Value.installed_version_id)}  physical={ShortId(kv.Value.physical_spec_id)}";
            }).ToList();
            if (lines.Count == 0)
            {
                lines.Add("No installed assets in Unslop.lock.json yet.");
            }

            _installedList.itemsSource = lines;
            _installedList.RefreshItems();
        }

        static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "—";
            }

            return id.Length <= 12 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 4);
        }

        async Task LoadProjectsAsync()
        {
            if (!BeginBusy("Loading projects…"))
            {
                return;
            }

            try
            {
                // Refresh Projects on Browse should also pick up a newly typed key.
                TryPersistKeyFromField(requireFieldValue: false, out _);
                if (!_binding.IsAuthenticated)
                {
                    SetMessage("No API key saved. Paste usk_… on Connect and click Test Connection.");
                    RefreshStatus();
                    return;
                }

                RefreshApi();
                _binding = BridgeServices.CreateBindingService();
                var page = await _binding.ListProjectsAsync().ConfigureAwait(true);
                await ContinueOnMainThread(() =>
                {
                    _projects = page?.data?.ToArray() ?? Array.Empty<ProjectDto>();
                    _projectList.itemsSource = _projects;
                    _projectList.RefreshItems();
                    if (_binding.NeedsReauthentication)
                    {
                        SetMessage(_binding.LastError ?? "Authorization failed. Re-paste the key and Test Connection again.");
                    }
                    else if (_projects.Length == 0)
                    {
                        SetMessage("Connected, but no projects were returned for this key.");
                    }
                    else
                    {
                        SetMessage($"Connected — loaded {_projects.Length} project(s). Select one to bind.");
                    }

                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List projects");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        async Task LoadAssetsAsync()
        {
            if (!BeginBusy("Loading assets…"))
            {
                return;
            }

            try
            {
                var projectId = _binding.BoundProjectId;
                if (string.IsNullOrEmpty(projectId))
                {
                    SetMessage("Bind a project first (Connect tab).");
                    return;
                }

                PersistApiBaseFromField();
                RefreshApi();
                var page = await _api.ListProjectAssetsAsync(projectId).ConfigureAwait(true);
                await ContinueOnMainThread(() =>
                {
                    _assets = page?.data?.ToArray() ?? Array.Empty<AssetSummaryDto>();
                    _assetList.itemsSource = _assets;
                    _assetList.RefreshItems();
                    _versions = Array.Empty<AssetVersionSummaryDto>();
                    _versionList.itemsSource = _versions;
                    _versionList.RefreshItems();
                    SetMessage($"Loaded {_assets.Length} asset(s).");
                    RefreshStatus();
                    RefreshPublishAssetChoices();
                });
            }
            catch (UnslopApiException ex) when (ex.IsUnauthorized)
            {
                await ContinueOnMainThread(() =>
                {
                    SetMessage("Authorization failed or was revoked. Update the API key on Connect.");
                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List assets");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        async Task LoadVersionsForSelectionAsync()
        {
            var idx = _assetList?.selectedIndex ?? -1;
            if (idx < 0 || idx >= _assets.Length)
            {
                return;
            }

            if (!BeginBusy("Loading versions…"))
            {
                return;
            }

            try
            {
                RefreshApi();
                var asset = _assets[idx];
                var page = await _api.ListAssetVersionsAsync(asset.asset_id).ConfigureAwait(true);
                await ContinueOnMainThread(() =>
                {
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

                    SetMessage($"Loaded {_versions.Length} version(s).");
                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "List versions");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
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

        async Task InstallSelectedAsync()
        {
            if (!BeginBusy("Installing…"))
            {
                return;
            }

            try
            {
                var aIdx = _assetList.selectedIndex;
                var vIdx = _versionList.selectedIndex;
                if (aIdx < 0 || aIdx >= _assets.Length)
                {
                    SetMessage("Select an asset first.");
                    return;
                }

                var asset = _assets[aIdx];
                var versionId = vIdx >= 0 && vIdx < _versions.Length
                    ? _versions[vIdx].asset_version_id
                    : asset.recommended_version_id;
                if (string.IsNullOrEmpty(versionId))
                {
                    SetMessage("No version selected or recommended.");
                    return;
                }

                if (!EditorUtility.DisplayDialog("Unslop", $"Install {asset.display_name}\nversion {versionId}?", "Install", "Cancel"))
                {
                    return;
                }

                RefreshApi();
                var progress = new Progress<string>(SetMessage);
                await new AssetInstallService(_api).InstallAsync(asset.asset_id, versionId, progress);
                await ContinueOnMainThread(() =>
                {
                    RefreshInstalled();
                    SetMessage("Install completed.");
                    EditorUtility.DisplayDialog("Unslop", "Install completed.", "OK");
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Install");
                await ContinueOnMainThread(() =>
                {
                    SetMessage(BridgeLog.Redact(ex.Message));
                    EditorUtility.DisplayDialog("Unslop", BridgeLog.Redact(ex.Message), "OK");
                });
            }
            finally
            {
                EndBusy();
            }
        }

        async Task CheckUpdatesAsync()
        {
            if (!BeginBusy("Checking updates…"))
            {
                return;
            }

            try
            {
                RefreshApi();
                var progress = new Progress<string>(SetMessage);
                var result = await new UpdateCheckService(_api).CheckInstalledAsync(progress);
                await ContinueOnMainThread(() =>
                {
                    var updates = result.Candidates.Where(c => c.HasUpdate).ToList();
                    if (updates.Count == 0)
                    {
                        SetMessage($"Update check OK — no updates ({result.Candidates.Count} asset(s)).");
                    }
                    else
                    {
                        var summary = string.Join("\n", updates.Select(u =>
                            $"{u.AssetId}: {u.InstalledVersionId} → {u.RecommendedVersionId} ({u.UpdateStatus})"));
                        SetMessage($"{updates.Count} update(s) available.\n{summary}");
                        EditorUtility.DisplayDialog("Unslop Updates", summary, "OK");
                    }

                    RefreshStatus();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Check updates");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        async Task AcceptCurrentScaleAsync()
        {
            await WriteCanonicalScaleAsync(acceptCurrent: true);
        }

        async Task SetCanonicalScaleAsync()
        {
            await WriteCanonicalScaleAsync(acceptCurrent: false);
        }

        async Task WriteCanonicalScaleAsync(bool acceptCurrent)
        {
            var wrapper = ResolveSelectedWrapper();
            if (wrapper == null)
            {
                SetMessage("Select an Unslop wrapper GameObject (with UnslopAssetReference) in the scene or Project.");
                return;
            }

            if (acceptCurrent && !CanonicalScaleService.ConfirmAcceptCurrentScale(wrapper))
            {
                return;
            }

            if (!BeginBusy(acceptCurrent ? "Accepting current scale…" : "Setting canonical scale…"))
            {
                return;
            }

            try
            {
                RefreshApi();
                var service = new CanonicalScaleService(_api);
                var result = acceptCurrent
                    ? await service.AcceptCurrentScaleAsync(wrapper)
                    : await service.SetCurrentSizeAsCanonicalAsync(wrapper);
                await ContinueOnMainThread(() =>
                {
                    SetMessage(result.Message);
                    if (result.Conflict412)
                    {
                        EditorUtility.DisplayDialog("Unslop", result.Message, "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Unslop",
                            $"{result.Message}\nMeasured: {result.MeasuredMetres}",
                            "OK");
                    }

                    RefreshInstalled();
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, acceptCurrent ? "Accept current scale" : "Set canonical scale");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        async Task ConfirmScaleAsync()
        {
            var wrapper = ResolveSelectedWrapper();
            if (wrapper == null)
            {
                SetMessage("Select an Unslop wrapper GameObject (with UnslopAssetReference) in the scene or Project.");
                return;
            }

            if (!BeginBusy("Confirming scale…"))
            {
                return;
            }

            try
            {
                RefreshApi();
                var result = await new ScaleConfirmationService(_api).ConfirmAsync(wrapper);
                await ContinueOnMainThread(() =>
                {
                    SetMessage($"{result.BadgeLabel} — {result.Message}");
                    EditorUtility.DisplayDialog("Unslop", $"{result.BadgeLabel}\n{result.Message}", "OK");
                });
            }
            catch (Exception ex)
            {
                BridgeLog.Exception(ex, "Confirm scale");
                await ContinueOnMainThread(() => SetMessage(BridgeLog.Redact(ex.Message)));
            }
            finally
            {
                EndBusy();
            }
        }

        static GameObject ResolveSelectedWrapper()
        {
            var go = Selection.activeGameObject;
            if (go != null
                && (go.GetComponent<UnslopAssetReference>() != null
                    || go.GetComponentInChildren<UnslopAssetReference>() != null
                    || go.GetComponentInParent<UnslopAssetReference>() != null))
            {
                var reference = go.GetComponent<UnslopAssetReference>()
                                ?? go.GetComponentInParent<UnslopAssetReference>()
                                ?? go.GetComponentInChildren<UnslopAssetReference>();
                return reference != null ? reference.gameObject : go;
            }

            if (Selection.activeObject is GameObject prefab
                && prefab.GetComponent<UnslopAssetReference>() != null)
            {
                return prefab;
            }

            return null;
        }

        void SetMessage(string message)
        {
            if (_messageLabel != null)
            {
                _messageLabel.text = message ?? string.Empty;
            }
        }

        bool BeginBusy(string message)
        {
            if (_busy)
            {
                SetMessage("Please wait for the current request to finish.");
                return false;
            }

            _busy = true;
            SetMessage(message);
            return true;
        }

        void EndBusy()
        {
            _busy = false;
            EditorApplication.delayCall += RefreshStatus;
        }

        static Task ContinueOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            return tcs.Task;
        }
    }
}
