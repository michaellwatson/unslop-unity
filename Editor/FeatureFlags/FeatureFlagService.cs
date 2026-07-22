using System.Collections.Generic;
using UnityEditor;

namespace Unslop.UnityBridge.Editor.FeatureFlags
{
    public static class FeatureFlagService
    {
        const string Prefix = "Unslop.Feature.";

        static readonly Dictionary<string, bool> Defaults = new Dictionary<string, bool>
        {
            ["unity_bridge_enabled"] = true,
            ["unity_bridge_updates_enabled"] = true,
            ["unity_bridge_material_resolution_v1"] = true,
            ["unity_bridge_canonical_scale_write"] = true,
            ["unity_bridge_scale_confirmation"] = true,
            ["unity_bridge_rollback"] = true
        };

        public static void EnsureDefaults()
        {
            foreach (var kv in Defaults)
            {
                if (!EditorPrefs.HasKey(Prefix + kv.Key))
                {
                    EditorPrefs.SetBool(Prefix + kv.Key, kv.Value);
                }
            }
        }

        public static bool IsEnabled(string flag)
        {
            EnsureDefaults();
            return EditorPrefs.GetBool(Prefix + flag, Defaults.TryGetValue(flag, out var d) && d);
        }

        public static void SetEnabled(string flag, bool enabled)
        {
            EditorPrefs.SetBool(Prefix + flag, enabled);
        }
    }
}
