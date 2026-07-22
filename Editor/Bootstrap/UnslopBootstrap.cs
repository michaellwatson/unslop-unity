using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.FeatureFlags;
using Unslop.UnityBridge.Editor.Settings;
using Unslop.UnityBridge.Editor.Transactions;
using UnityEditor;

namespace Unslop.UnityBridge.Editor.Bootstrap
{
    [InitializeOnLoad]
    static class UnslopBootstrap
    {
        static UnslopBootstrap()
        {
            EditorApplication.delayCall += OnEditorReady;
        }

        static void OnEditorReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += OnEditorReady;
                return;
            }

            UnslopProjectSettings.EnsureExists();
            FeatureFlagService.EnsureDefaults();

            var recovery = TransactionRecovery.TryRecoverIncompleteJournals();
            if (recovery.Attempted)
            {
                BridgeLog.Info($"Transaction recovery: {recovery.Summary}");
            }
        }
    }
}
