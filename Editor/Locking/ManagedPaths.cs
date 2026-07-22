using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Locking
{
    public static class ManagedPaths
    {
        public const string AssetsRoot = "Assets/Unslop";
        public const string InstalledRoot = "Assets/Unslop/Installed";
        public const string StagingRoot = "Assets/Unslop/__Staging";
        public const string SettingsRoot = "Assets/Unslop/Settings";
        public const string LockFileName = "Unslop.lock.json";
        public const string LegacyWrapperPrefabFileName = "Asset.prefab";
        public const string LegacyVisualPrefabFileName = "Visual.prefab";

        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string LockFilePath =>
            Path.Combine(ProjectRoot, LockFileName);

        public static string LibraryRoot =>
            Path.Combine(ProjectRoot, "Library", "Unslop");

        public static string AuthDir => Path.Combine(LibraryRoot, "Auth");
        public static string CacheDir => Path.Combine(LibraryRoot, "Cache");
        public static string DownloadsDir => Path.Combine(LibraryRoot, "Downloads");
        public static string TransactionsDir => Path.Combine(LibraryRoot, "Transactions");
        public static string LocksDir => Path.Combine(LibraryRoot, "Locks");
        public static string DiagnosticsDir => Path.Combine(LibraryRoot, "Diagnostics");

        /// <summary>
        /// Resolves the installed asset folder. Prefers an existing folder (GUID or friendly),
        /// otherwise uses <c>DisplayName_abcd1234</c>.
        /// </summary>
        public static string InstalledAssetDir(string assetId, string displayName = null)
        {
            var existing = FindExistingInstalledDir(assetId);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            return $"{InstalledRoot}/{PreferredInstalledFolderName(assetId, displayName)}";
        }

        public static string PreferredInstalledFolderName(string assetId, string displayName)
        {
            var shortKey = ShortAssetKey(assetId);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return SanitizeId(assetId);
            }

            return $"{SanitizeDisplayName(displayName)}_{shortKey}";
        }

        public static string FindExistingInstalledDir(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return null;
            }

            var legacy = $"{InstalledRoot}/{SanitizeId(assetId)}";
            if (Directory.Exists(ToFull(legacy)))
            {
                return legacy.Replace('\\', '/');
            }

            var shortKey = ShortAssetKey(assetId);
            var installedFull = ToFull(InstalledRoot);
            if (!Directory.Exists(installedFull))
            {
                return null;
            }

            foreach (var dir in Directory.GetDirectories(installedFull))
            {
                var name = Path.GetFileName(dir);
                if (name.EndsWith("_" + shortKey, StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf(SanitizeId(assetId), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return $"{InstalledRoot}/{name}".Replace('\\', '/');
                }
            }

            return null;
        }

        /// <summary>
        /// If a GUID folder exists and a friendly name is available, rename it in the AssetDatabase.
        /// </summary>
        public static string EnsureFriendlyInstalledDir(string assetId, string displayName)
        {
            var preferred = $"{InstalledRoot}/{PreferredInstalledFolderName(assetId, displayName)}";
            var existing = FindExistingInstalledDir(assetId);
            if (string.IsNullOrEmpty(existing))
            {
                EnsureDirectory(preferred);
                return preferred;
            }

            if (string.Equals(existing, preferred, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(displayName))
            {
                return existing;
            }

            // Only auto-rename pure GUID folders to friendly names.
            if (!string.Equals(Path.GetFileName(existing), SanitizeId(assetId), StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }

            if (Directory.Exists(ToFull(preferred)))
            {
                return preferred;
            }

            var result = AssetDatabase.MoveAsset(existing, preferred);
            if (string.IsNullOrEmpty(result))
            {
                AssetDatabase.Refresh();
                return preferred;
            }

            // Move failed — keep existing.
            return existing;
        }

        public static string StagingAssetVersionDir(string assetId, string versionId) =>
            $"{StagingRoot}/{SanitizeId(assetId)}/{SanitizeId(versionId)}";

        public static string LocalMetadataPath(string assetId, string displayName = null) =>
            $"{InstalledAssetDir(assetId, displayName)}/asset.local.json";

        public static string DownloadVersionDir(string versionId) =>
            Path.Combine(DownloadsDir, SanitizeId(versionId));

        public static string PrefabsDir(string installedRoot) =>
            $"{installedRoot.TrimEnd('/')}/Prefabs";

        public static string ResolveWrapperPrefabPath(string installedRoot, string displayName = null)
        {
            var prefabs = PrefabsDir(installedRoot);
            var legacy = $"{prefabs}/{LegacyWrapperPrefabFileName}";
            if (File.Exists(ToFull(legacy)) || AssetDatabase.LoadAssetAtPath<GameObject>(legacy) != null)
            {
                return legacy;
            }

            var friendly = $"{prefabs}/{SanitizeDisplayName(displayName)}.prefab";
            if (!string.IsNullOrWhiteSpace(displayName) && File.Exists(ToFull(friendly)))
            {
                return friendly;
            }

            // Any other *.prefab that is not the Visual prefab.
            var fullPrefabs = ToFull(prefabs);
            if (Directory.Exists(fullPrefabs))
            {
                foreach (var file in Directory.GetFiles(fullPrefabs, "*.prefab"))
                {
                    var name = Path.GetFileName(file);
                    if (name.EndsWith("_Visual.prefab", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, LegacyVisualPrefabFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return $"{prefabs}/{name}".Replace('\\', '/');
                }
            }

            return string.IsNullOrWhiteSpace(displayName)
                ? legacy
                : friendly;
        }

        public static string ResolveVisualPrefabPath(string installedRoot, string displayName = null)
        {
            var prefabs = PrefabsDir(installedRoot);
            var legacy = $"{prefabs}/{LegacyVisualPrefabFileName}";
            if (File.Exists(ToFull(legacy)) || AssetDatabase.LoadAssetAtPath<GameObject>(legacy) != null)
            {
                return legacy;
            }

            var friendly = $"{prefabs}/{SanitizeDisplayName(displayName)}_Visual.prefab";
            if (!string.IsNullOrWhiteSpace(displayName) && File.Exists(ToFull(friendly)))
            {
                return friendly;
            }

            var fullPrefabs = ToFull(prefabs);
            if (Directory.Exists(fullPrefabs))
            {
                foreach (var file in Directory.GetFiles(fullPrefabs, "*_Visual.prefab"))
                {
                    return $"{prefabs}/{Path.GetFileName(file)}".Replace('\\', '/');
                }
            }

            return string.IsNullOrWhiteSpace(displayName)
                ? legacy
                : friendly;
        }

        public static string PreferredWrapperPrefabPath(string installedRoot, string displayName) =>
            string.IsNullOrWhiteSpace(displayName)
                ? $"{PrefabsDir(installedRoot)}/{LegacyWrapperPrefabFileName}"
                : $"{PrefabsDir(installedRoot)}/{SanitizeDisplayName(displayName)}.prefab";

        public static string PreferredVisualPrefabPath(string installedRoot, string displayName) =>
            string.IsNullOrWhiteSpace(displayName)
                ? $"{PrefabsDir(installedRoot)}/{LegacyVisualPrefabFileName}"
                : $"{PrefabsDir(installedRoot)}/{SanitizeDisplayName(displayName)}_Visual.prefab";

        public static string ShortAssetKey(string assetId)
        {
            var compact = SanitizeId(assetId).Replace("-", string.Empty);
            if (string.IsNullOrEmpty(compact))
            {
                return "unknown";
            }

            return compact.Length <= 8 ? compact : compact.Substring(0, 8);
        }

        public static string SanitizeDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "UnslopAsset";
            }

            var sb = new StringBuilder(displayName.Trim().Length);
            foreach (var c in displayName.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (c == ' ' || c == '-' || c == '_' || c == '.')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    {
                        sb.Append('_');
                    }
                }
            }

            var result = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(result))
            {
                result = "UnslopAsset";
            }

            if (result.Length > 48)
            {
                result = result.Substring(0, 48).Trim('_');
            }

            return result;
        }

        public static string SanitizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "_unknown";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                id = id.Replace(c, '_');
            }

            return id;
        }

        public static string ToFull(string assetPath) =>
            Path.Combine(ProjectRoot, (assetPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (path.StartsWith("Assets/") || path.StartsWith("Assets\\"))
            {
                Directory.CreateDirectory(ToFull(path));
                return;
            }

            Directory.CreateDirectory(path);
        }

        public static void EnsureOperationalDirectories()
        {
            EnsureDirectory(AuthDir);
            EnsureDirectory(CacheDir);
            EnsureDirectory(Path.Combine(CacheDir, "manifests"));
            EnsureDirectory(Path.Combine(CacheDir, "analysis"));
            EnsureDirectory(Path.Combine(CacheDir, "previews"));
            EnsureDirectory(DownloadsDir);
            EnsureDirectory(TransactionsDir);
            EnsureDirectory(LocksDir);
            EnsureDirectory(DiagnosticsDir);
            EnsureDirectory(InstalledRoot);
            EnsureDirectory(StagingRoot);
            EnsureDirectory(SettingsRoot);
        }
    }
}
