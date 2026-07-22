using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Tests
{
    public sealed class ManifestValidatorTests
    {
        [Test]
        public void SampleFixture_IsValid()
        {
            var path = Path.GetFullPath("Packages/com.unslop.unity-bridge/Tests/Fixtures/sample_asset_manifest.json");
            if (!File.Exists(path))
            {
                // When package is at repo root via disk install, fixtures resolve relative to package dir.
                path = Path.Combine(Application.dataPath, "..", "Packages", "com.unslop.unity-bridge", "Tests", "Fixtures", "sample_asset_manifest.json");
            }

            // Fallback: search from common package roots
            if (!File.Exists(path))
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(Directory.GetCurrentDirectory(), "Tests", "Fixtures", "sample_asset_manifest.json"),
                             Path.Combine(Application.dataPath, "..", "Tests", "Fixtures", "sample_asset_manifest.json")
                         })
                {
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        break;
                    }
                }
            }

            Assert.That(File.Exists(path), Is.True, "fixture missing: " + path);
            var manifest = JsonConvert.DeserializeObject<AssetVersionManifest>(File.ReadAllText(path));
            var report = new ManifestValidator().ValidateManifest(manifest);
            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
        }

        [Test]
        public void RejectsScriptPayload()
        {
            var manifest = new AssetVersionManifest
            {
                schema_version = 1,
                asset_id = "a",
                asset_version_id = "v",
                display_name = "t",
                content_kind = "static_mesh",
                minimum_bridge_version = "1.0.0",
                model = new ModelManifest { file_id = "m", relative_path = "model.fbx", format = "fbx" },
                files =
                {
                    new ManifestFile { file_id = "m", role = "model", relative_path = "model.fbx", byte_length = 10, sha256 = "abc" },
                    new ManifestFile { file_id = "x", role = "texture", relative_path = "Evil.cs", byte_length = 10, sha256 = "abc" }
                }
            };
            var report = new ManifestValidator().ValidateManifest(manifest);
            Assert.That(report.IsValid, Is.False);
            Assert.That(string.Join(" ", report.Errors), Does.Contain("Forbidden"));
        }
    }
}
