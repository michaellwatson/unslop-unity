using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unslop.UnityBridge.Editor.Locking;

namespace Unslop.UnityBridge.Editor.Tests
{
    public sealed class LockFileServiceTests
    {
        [Test]
        public void CanonicalSerialize_OrdersKeysAndEndsWithNewline()
        {
            var json = LockFileService.CanonicalSerialize(new
            {
                zebra = 1,
                alpha = 2,
                nested = new { z = true, a = false }
            });

            Assert.IsTrue(json.EndsWith("\n"));
            var alphaIdx = json.IndexOf("\"alpha\"");
            var zebraIdx = json.IndexOf("\"zebra\"");
            Assert.Greater(alphaIdx, -1);
            Assert.Greater(zebraIdx, alphaIdx);
        }

        [Test]
        public void ComputeStateHash_IsStableForSameEntry()
        {
            var entry = new LockAssetEntry
            {
                installed_version_id = "av_1",
                installed_version_number = 1,
                physical_spec_id = "ps_1",
                wrapper_prefab_guid = "guid_w",
                visual_prefab_guid = "guid_v",
                source_fbx_guid = "guid_f",
                import_profile_hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                manifest_sha256 = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                material_bindings = new Dictionary<string, LockMaterialBinding>
                {
                    ["slot_b"] = new LockMaterialBinding { mode = "managed", material_guid = "m2" },
                    ["slot_a"] = new LockMaterialBinding { mode = "managed", material_guid = "m1" }
                }
            };

            var h1 = LockFileService.ComputeStateHash(entry);
            var h2 = LockFileService.ComputeStateHash(entry);
            Assert.AreEqual(h1, h2);
            Assert.IsTrue(h1.StartsWith("sha256:"));
        }

        [Test]
        public void ComputeStateHash_ChangesWhenVersionChanges()
        {
            var entry = new LockAssetEntry
            {
                installed_version_id = "av_1",
                installed_version_number = 1,
                manifest_sha256 = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            };
            var first = LockFileService.ComputeStateHash(entry);
            entry.installed_version_id = "av_2";
            var second = LockFileService.ComputeStateHash(entry);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void LoadOrCreate_ThenSave_RoundTripsHeader()
        {
            var projectId = "test-project-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            var backupPath = ManagedPaths.LockFilePath + ".testbak";
            var hadLock = File.Exists(ManagedPaths.LockFilePath);
            if (hadLock)
            {
                File.Copy(ManagedPaths.LockFilePath, backupPath, true);
            }

            try
            {
                if (File.Exists(ManagedPaths.LockFilePath))
                {
                    File.Delete(ManagedPaths.LockFilePath);
                }

                var created = LockFileService.LoadOrCreate(projectId, "staging");
                Assert.AreEqual(projectId, created.project_id);
                Assert.AreEqual("staging", created.environment);
                Assert.IsTrue(File.Exists(ManagedPaths.LockFilePath));

                created.assets["ast_demo"] = new LockAssetEntry
                {
                    installed_version_id = "av_demo",
                    installed_version_number = 1
                };
                LockFileService.UpsertAsset(created, "ast_demo", created.assets["ast_demo"]);

                var reloaded = LockFileService.LoadOrCreate(projectId, "staging");
                Assert.AreEqual(projectId, reloaded.project_id);
                Assert.IsTrue(reloaded.assets.ContainsKey("ast_demo"));
                Assert.IsFalse(string.IsNullOrEmpty(reloaded.assets["ast_demo"].state_hash));
            }
            finally
            {
                if (File.Exists(ManagedPaths.LockFilePath))
                {
                    File.Delete(ManagedPaths.LockFilePath);
                }

                if (hadLock && File.Exists(backupPath))
                {
                    File.Move(backupPath, ManagedPaths.LockFilePath);
                }
                else if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                var bak = ManagedPaths.LockFilePath + ".bak";
                if (File.Exists(bak))
                {
                    File.Delete(bak);
                }
            }
        }
    }
}
