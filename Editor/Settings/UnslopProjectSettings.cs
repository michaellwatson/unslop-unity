using System.IO;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Locking;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Settings
{
    public sealed class UnslopProjectSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Unslop/Settings/UnslopProjectSettings.asset";

        [SerializeField] string apiBaseUrl = PackageInfo.DefaultApiBaseUrl;
        [SerializeField] string boundProjectId = string.Empty;
        [SerializeField] string boundProjectName = string.Empty;
        [SerializeField] string environment = "production";
        [SerializeField] bool deferredUpdateChecks = true;

        public string ApiBaseUrl
        {
            get => string.IsNullOrWhiteSpace(apiBaseUrl) ? PackageInfo.DefaultApiBaseUrl : apiBaseUrl.TrimEnd('/');
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
                }
            };
        }
    }
}
