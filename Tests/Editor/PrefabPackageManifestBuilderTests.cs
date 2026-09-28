using System.Collections.Generic;
using NUnit.Framework;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Manifests;
using Unslop.UnityBridge.Editor.Publishing;

namespace Unslop.UnityBridge.Editor.Tests
{
    public sealed class PrefabPackageManifestBuilderTests
    {
        [Test]
        public void BuildAssetJson_IncludesPipelineOriginAndModel()
        {
            var model = new UploadedFileDto
            {
                file_id = "model-id",
                relative_path = "model.fbx",
                role = "model",
                byte_length = 12,
                sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            };
            var tex = new UploadedFileDto
            {
                file_id = "tex-id",
                relative_path = "textures/albedo.png",
                role = "texture",
                byte_length = 4,
                sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            };

            var json = PrefabPackageManifestBuilder.BuildAssetJson(
                "asset-1",
                "version-1",
                "Test Chair",
                model,
                new List<UploadedFileDto> { model, tex });

            Assert.AreEqual(1, json["schema_version"]);
            Assert.AreEqual("asset-1", json["asset_id"]);
            Assert.AreEqual("version-1", json["asset_version_id"]);
            Assert.AreEqual("Test Chair", json["display_name"]);
            Assert.AreEqual("static_mesh", json["content_kind"]);

            var modelBlock = (Dictionary<string, object>)json["model"];
            Assert.AreEqual("model-id", modelBlock["file_id"]);
            Assert.AreEqual("fbx", modelBlock["format"]);

            var compatibility = (Dictionary<string, object>)json["compatibility"];
            Assert.AreEqual(PrefabPackageManifestBuilder.PipelineOrigin, compatibility["pipeline_origin"]);

            var files = (List<Dictionary<string, object>>)json["files"];
            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("model.fbx", files[0]["relative_path"]);
        }

        [Test]
        public void PatchAssetJsonFileRow_ReplacesOrAddsAssetManifest()
        {
            var model = new UploadedFileDto
            {
                file_id = "model-id",
                relative_path = "model.fbx",
                role = "model",
                byte_length = 1,
                sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            };
            var json = PrefabPackageManifestBuilder.BuildAssetJson(
                "a", "v", "Name", model, new List<UploadedFileDto> { model });

            var first = new UploadedFileDto
            {
                file_id = "asset-file-1",
                relative_path = "asset.json",
                role = "asset_manifest",
                byte_length = 100,
                sha256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            };
            PrefabPackageManifestBuilder.PatchAssetJsonFileRow(json, first);

            var files = (List<Dictionary<string, object>>)json["files"];
            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("asset-file-1", files[1]["file_id"]);

            var second = new UploadedFileDto
            {
                file_id = "asset-file-2",
                relative_path = "asset.json",
                role = "asset_manifest",
                byte_length = 120,
                sha256 = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
            };
            PrefabPackageManifestBuilder.PatchAssetJsonFileRow(json, second);
            files = (List<Dictionary<string, object>>)json["files"];
            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("asset-file-2", files[1]["file_id"]);
            Assert.AreEqual(120L, files[1]["byte_length"]);
        }

        [Test]
        public void ToMaterialsDictionary_PreservesSlotsAndTextures()
        {
            var materials = new MaterialsManifest
            {
                schema_version = 1,
                materials =
                {
                    new MaterialDefinition
                    {
                        material_id = "mat_body",
                        display_name = "Body",
                        model = "metallic_roughness",
                        textures = new Dictionary<string, string> { ["base_color"] = "textures/albedo.png" }
                    }
                },
                slots =
                {
                    new MaterialSlot
                    {
                        slot_id = "slot_body",
                        display_name = "Body",
                        material_id = "mat_body"
                    }
                }
            };

            var dict = PrefabPackageManifestBuilder.ToMaterialsDictionary(materials);
            Assert.AreEqual(1, dict["schema_version"]);
            var mats = (List<Dictionary<string, object>>)dict["materials"];
            Assert.AreEqual("mat_body", mats[0]["material_id"]);
            var textures = (Dictionary<string, string>)mats[0]["textures"];
            Assert.AreEqual("textures/albedo.png", textures["base_color"]);
            var slots = (List<Dictionary<string, object>>)dict["slots"];
            Assert.AreEqual("slot_body", slots[0]["slot_id"]);
        }
    }
}
