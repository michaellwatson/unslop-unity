using System.IO;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Locking;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Settings
{
    public sealed class UnslopProjectSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Unslop/Settings/UnslopProjectSettings.asset";

        [SerializeField] string apiBaseUrl = BridgePackageInfo.DefaultApiBaseUrl;
        [SerializeField] string boundProjectId = string.Empty;
        [SerializeField] string boundProjectName = string.Empty;
        [SerializeField] string environment = "production";
        [SerializeField] bool deferredUpdateChecks = true;

        public string ApiBaseUrl
        {
            get => string.IsNullOrWhiteSpace(apiBaseUrl) ? BridgePackageInfo.DefaultApiBaseUrl : apiBaseUrl.TrimEnd('/');
            set => apiBaseUrl = value;
        }

        public string BoundProjectId
        {
            get => boundProjectId;
            set => boundProjectId = value ?? string.Empty;
        }

        public string BoundProjectName
        {
            get => boundProjectName;
            set => boundProjectName = value ?? string.Empty;
        }

        public string Environment
        {
            get => environment;
            set => environment = string.IsNullOrWhiteSpace(value) ? "production" : value;
        }

        public bool DeferredUpdateChecks
        {
            get => deferredUpdateChecks;
            set => deferredUpdateChecks = value;
        }

        public static UnslopProjectSettings EnsureExists()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnslopProjectSettings>(AssetPath);
            if (existing != null)
            {
                return existing;
            }

            ManagedPaths.EnsureDirectory(Path.GetDirectoryName(AssetPath));
            var created = CreateInstance<UnslopProjectSettings>();
            AssetDatabase.CreateAsset(created, AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        public static UnslopProjectSettings GetOrNull()
        {
            return AssetDatabase.LoadAssetAtPath<UnslopProjectSettings>(AssetPath);
        }
    }

    static class UnslopSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unslop", SettingsScope.Project)
            {
                label = "Unslop",
                guiHandler = _ =>
                {
                    var settings = UnslopProjectSettings.EnsureExists();
                    var so = new SerializedObject(settings);
                    EditorGUILayout.PropertyField(so.FindProperty("apiBaseUrl"), new GUIContent("API Base URL"));
                    EditorGUILayout.PropertyField(so.FindProperty("boundProjectId"), new GUIContent("Bound Project Id"));
                    EditorGUILayout.PropertyField(so.FindProperty("boundProjectName"), new GUIContent("Bound Project Name"));
                    EditorGUILayout.PropertyField(so.FindProperty("environment"), new GUIContent("Environment"));
                    EditorGUILayout.PropertyField(so.FindProperty("deferredUpdateChecks"), new GUIContent("Deferred Update Checks"));
                    so.ApplyModifiedProperties();

                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
                    var debug = EditorGUILayout.Toggle(
                        new GUIContent(
                            "Debug logging",
                            "Verbose Console + local log; uploads to bound project via Bridge diagnostics API"),
                        BridgeDebugMode.Enabled);
                    if (debug != BridgeDebugMode.Enabled)
                    {
                        BridgeDebugMode.Enabled = debug;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Reveal Debug Log", GUILayout.Width(140)))
                        {
                            BridgeDebugMode.RevealLog();
                        }

                        if (GUILayout.Button("Clear Debug Log", GUILayout.Width(140)))
                        {
                            BridgeDebugMode.ClearLog();
                        }

                        if (GUILayout.Button("Upload Debug Log", GUILayout.Width(140)))
                        {
                            BridgeDebugUploader.TryFlushFireAndForget(isFinal: true);
                        }

                        if (GUILayout.Button("Pull Latest", GUILayout.Width(100)))
                        {
                            _ = PullLatestFromSettings();
                        }
                    }
                }
            };
        }

        static async System.Threading.Tasks.Task PullLatestFromSettings()
        {
            try
            {
                var path = await BridgeDebugUploader.PullLatestAsync();
                EditorUtility.RevealInFinder(path);
            }
            catch (System.Exception ex)
            {
                BridgeLog.Warn("Pull debug log failed: " + BridgeLog.Redact(ex.Message));
            }
        }
    }
}
