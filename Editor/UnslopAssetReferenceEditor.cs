using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Scale;
using Unslop.UnityBridge.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor
{
    [CustomEditor(typeof(UnslopAssetReference))]
    public sealed class UnslopAssetReferenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var reference = (UnslopAssetReference)target;
            var local = string.IsNullOrEmpty(reference.AssetId)
                ? null
                : LockFileService.LoadLocalMetadata(reference.AssetId);
            var lockEntry = TryLockEntry(reference.AssetId);
            var displayName = FirstNonEmpty(
                local?.display_name,
                lockEntry?.display_name,
                ResolveFolderLabel(reference.AssetId),
                "Unslop Asset");

            EditorGUILayout.LabelField("Display Name", displayName);
            EditorGUILayout.Space(4);

            DrawReadonly("Asset Id", reference.AssetId);
            DrawReadonly("Installed Version", ShortId(reference.InstalledVersionId));
            DrawReadonly(
                "Physical Spec",
                string.IsNullOrEmpty(reference.PhysicalSpecId)
                    ? "(none — use Set Canonical Scale)"
                    : ShortId(reference.PhysicalSpecId));
            DrawReadonly("Wrapper Prefab", ShortId(reference.WrapperPrefabGuid));

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping Prefab"))
                {
                    PingWrapper(reference);
                }

                if (GUILayout.Button("Copy Asset Id"))
                {
                    EditorGUIUtility.systemCopyBuffer = reference.AssetId ?? string.Empty;
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Scale: optionally resize VisualCorrection to real-world size, then Set Canonical Scale. " +
                "That writes the measured size online and resets VisualCorrection to 1,1,1. Confirm Scale afterwards.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Set Canonical Scale", GUILayout.Height(28)))
                {
                    _ = RunCanonicalScale(reference.gameObject);
                }

                if (GUILayout.Button("Confirm Scale"))
                {
                    _ = RunConfirmScale(reference.gameObject);
                }
            }

            EditorGUILayout.Space(8);
            if (EditorGUILayout.Foldout(SessionState.GetBool("Unslop.ShowRawIds", false), "Raw IDs", true))
            {
                SessionState.SetBool("Unslop.ShowRawIds", true);
                DrawDefaultInspector();
            }
            else
            {
                SessionState.SetBool("Unslop.ShowRawIds", false);
            }
        }

        static async Task RunCanonicalScale(GameObject wrapper)
        {
            try
            {
                var result = await new CanonicalScaleService().SetCurrentSizeAsCanonicalAsync(wrapper);
                EditorUtility.DisplayDialog("Unslop", result.Message, "OK");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Unslop", ex.Message, "OK");
            }
        }

        static async Task RunConfirmScale(GameObject wrapper)
        {
            try
            {
                var result = await new ScaleConfirmationService().ConfirmAsync(wrapper);
                EditorUtility.DisplayDialog("Unslop", $"{result.BadgeLabel}\n{result.Message}", "OK");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Unslop", ex.Message, "OK");
            }
        }

        static LockAssetEntry TryLockEntry(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return null;
            }

            var settings = BridgeServices.Settings;
            var lockFile = LockFileService.LoadOrCreate(settings.BoundProjectId, settings.Environment);
            return lockFile.assets != null && lockFile.assets.TryGetValue(assetId, out var entry) ? entry : null;
        }

        static void PingWrapper(UnslopAssetReference reference)
        {
            if (!string.IsNullOrEmpty(reference.WrapperPrefabGuid))
            {
                var path = AssetDatabase.GUIDToAssetPath(reference.WrapperPrefabGuid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    return;
                }
            }

            EditorGUIUtility.PingObject(reference.gameObject);
        }

        static string ResolveFolderLabel(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return null;
            }

            var path = ManagedPaths.InstalledAssetDir(assetId);
            return System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        }

        static void DrawReadonly(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, value ?? string.Empty);
            }
        }

        static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return id.Length <= 12 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 4);
        }

        static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }
}
