using System.IO;
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

        public static string InstalledAssetDir(string assetId) =>
            $"{InstalledRoot}/{SanitizeId(assetId)}";

        public static string StagingAssetVersionDir(string assetId, string versionId) =>
            $"{StagingRoot}/{SanitizeId(assetId)}/{SanitizeId(versionId)}";

        public static string LocalMetadataPath(string assetId) =>
            $"{InstalledAssetDir(assetId)}/asset.local.json";

        public static string DownloadVersionDir(string versionId) =>
            Path.Combine(DownloadsDir, SanitizeId(versionId));

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

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (path.StartsWith("Assets/") || path.StartsWith("Assets\\"))
            {
                var full = Path.Combine(ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(full);
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
