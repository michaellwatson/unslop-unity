using System;
using System.IO;
using Unslop.UnityBridge.Editor.Diagnostics;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using Unslop.UnityBridge.Editor.Security;
using UnityEditor;

namespace Unslop.UnityBridge.Editor.Importing
{
    public sealed class StagingMaterialiseResult
    {
        public string StagingAssetPath { get; set; }
        public string ImportProfileHash { get; set; }
        public string ModelAssetPath { get; set; }
    }

    public static class StagingMaterialiser
    {
        public static StagingMaterialiseResult Materialise(
            string downloadRoot,
            AssetVersionManifest manifest,
            string assetId = null,
            string versionId = null)
        {
            if (string.IsNullOrWhiteSpace(downloadRoot) || !Directory.Exists(downloadRoot))
            {
                throw new DirectoryNotFoundException("Download root not found: " + downloadRoot);
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            PackageContentGuard.ValidateDownloadedPackage(downloadRoot, manifest);

            assetId ??= manifest.asset_id;
            versionId ??= manifest.asset_version_id;
            var stagingRelative = ManagedPaths.StagingAssetVersionDir(assetId, versionId);
            var stagingFull = Path.Combine(
                ManagedPaths.ProjectRoot,
                stagingRelative.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(stagingFull))
            {
                FileUtil.DeleteFileOrDirectory(stagingFull);
                FileUtil.DeleteFileOrDirectory(stagingFull + ".meta");
            }

            ManagedPaths.EnsureDirectory(stagingRelative);
            CopyDirectory(downloadRoot, stagingFull);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var file in manifest.files)
            {
                PackageContentGuard.EnsureSafeRelativePath(file.relative_path);
                var assetPath = $"{stagingRelative}/{PackageContentGuard.NormalizePath(file.relative_path)}";
                ApplyImporterSettings(assetPath, file.role, file.relative_path);
            }

            AssetDatabase.ImportAsset(stagingRelative, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);

            var importHash = ImportProfile.ComputeImportProfileHash();
            var modelRel = manifest.model?.relative_path ?? "model.fbx";
            var modelPath = $"{stagingRelative}/{PackageContentGuard.NormalizePath(modelRel)}";

            BridgeLog.Info($"Staged package at {stagingRelative} (import_profile_hash={importHash}).");
            return new StagingMaterialiseResult
            {
                StagingAssetPath = stagingRelative,
                ImportProfileHash = importHash,
                ModelAssetPath = modelPath
            };
        }

        static void ApplyImporterSettings(string assetPath, string role, string relativePath)
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer is ModelImporter modelImporter)
            {
                ImportProfile.ApplyModelImporter(modelImporter);
                modelImporter.SaveAndReimport();
                return;
            }

            if (importer is TextureImporter textureImporter)
            {
                ImportProfile.ApplyTextureImporter(textureImporter, relativePath);
                textureImporter.SaveAndReimport();
            }
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, dir);
                PackageContentGuard.EnsureSafeRelativePath(rel.Replace('\\', '/'));
                Directory.CreateDirectory(Path.Combine(destination, rel));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file).Replace('\\', '/');
                PackageContentGuard.EnsureSafeRelativePath(rel);
                var dest = Path.Combine(destination, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? destination);
                File.Copy(file, dest, true);
            }
        }
    }
}
