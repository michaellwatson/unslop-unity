using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unslop.UnityBridge.Editor.Downloads;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Manifests;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Analysis
{
    public enum DiffKind
    {
        Unchanged,
        Added,
        Removed,
        Changed
    }

    public sealed class DiffEntry
    {
        public string Category { get; set; }
        public string Path { get; set; }
        public DiffKind Kind { get; set; }
        public string Detail { get; set; }
    }

    public sealed class AssetDiffReport
    {
        public string AssetId { get; set; }
        public string InstalledVersionId { get; set; }
        public string CandidateVersionId { get; set; }
        public List<DiffEntry> Entries { get; } = new List<DiffEntry>();
        public Vector3 InstalledBoundsSize { get; set; }
        public Vector3 CandidateBoundsSize { get; set; }
        public Vector3 InstalledPivot { get; set; }
        public Vector3 CandidatePivot { get; set; }
        public bool HierarchyCompatible { get; set; } = true;
        public bool MaterialSlotsCompatible { get; set; } = true;
        public bool HasBlockingChanges => Entries.Any(e =>
            e.Kind != DiffKind.Unchanged
            && (e.Category == "hierarchy" || e.Category == "geometry"));

        public int ChangedCount => Entries.Count(e => e.Kind != DiffKind.Unchanged);
    }

    /// <summary>
    /// Compares installed vs candidate manifests, materials, hierarchy, bounds, and pivot summaries.
    /// </summary>
    public static class AssetDiffEngine
    {
        public static AssetDiffReport Compare(
            string assetId,
            string installedVersionId,
            AssetVersionManifest installedManifest,
            MaterialsManifest installedMaterials,
            string candidateVersionId,
            AssetVersionManifest candidateManifest,
            MaterialsManifest candidateMaterials,
            GameObject installedRoot = null,
            GameObject candidateRoot = null)
        {
            var report = new AssetDiffReport
            {
                AssetId = assetId,
                InstalledVersionId = installedVersionId,
                CandidateVersionId = candidateVersionId,
                HierarchyCompatible = candidateManifest?.compatibility?.hierarchy_compatible ?? true,
                MaterialSlotsCompatible = candidateManifest?.compatibility?.material_slots_compatible ?? true
            };

            CompareFiles(report, installedManifest, candidateManifest);
            CompareMaterials(report, installedMaterials, candidateMaterials);
            CompareHierarchy(report, installedRoot, candidateRoot);
            CompareBoundsAndPivot(report, installedRoot, candidateRoot);

            if (candidateManifest?.compatibility?.declared_changes != null)
            {
                foreach (var change in candidateManifest.compatibility.declared_changes)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "declared",
                        Path = change,
                        Kind = DiffKind.Changed,
                        Detail = "Publisher-declared change"
                    });
                }
            }

            return report;
        }

        public static AssetDiffReport CompareInstalledToStaging(
            string assetId,
            LockAssetEntry installedEntry,
            AssetVersionManifest installedManifest,
            MaterialsManifest installedMaterials,
            string candidateVersionId,
            AssetVersionManifest candidateManifest,
            MaterialsManifest candidateMaterials,
            string installedModelPath,
            string candidateModelPath)
        {
            var installedGo = LoadHierarchy(installedModelPath);
            var candidateGo = LoadHierarchy(candidateModelPath);
            try
            {
                return Compare(
                    assetId,
                    installedEntry?.installed_version_id,
                    installedManifest,
                    installedMaterials,
                    candidateVersionId,
                    candidateManifest,
                    candidateMaterials,
                    installedGo,
                    candidateGo);
            }
            finally
            {
                if (installedGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(installedGo);
                }

                if (candidateGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(candidateGo);
                }
            }
        }

        static void CompareFiles(AssetDiffReport report, AssetVersionManifest installed, AssetVersionManifest candidate)
        {
            var left = (installed?.files ?? new List<ManifestFile>())
                .ToDictionary(f => NormalizePath(f.relative_path), f => f, StringComparer.OrdinalIgnoreCase);
            var right = (candidate?.files ?? new List<ManifestFile>())
                .ToDictionary(f => NormalizePath(f.relative_path), f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var path in left.Keys.Union(right.Keys).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                left.TryGetValue(path, out var a);
                right.TryGetValue(path, out var b);
                if (a == null)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = RoleCategory(b.role),
                        Path = path,
                        Kind = DiffKind.Added,
                        Detail = $"role={b.role} sha={HashUtil.Normalize(b.sha256)}"
                    });
                    continue;
                }

                if (b == null)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = RoleCategory(a.role),
                        Path = path,
                        Kind = DiffKind.Removed,
                        Detail = $"role={a.role}"
                    });
                    continue;
                }

                var kind = HashUtil.EqualsHash(a.sha256, b.sha256) && a.byte_length == b.byte_length
                    ? DiffKind.Unchanged
                    : DiffKind.Changed;
                report.Entries.Add(new DiffEntry
                {
                    Category = RoleCategory(b.role ?? a.role),
                    Path = path,
                    Kind = kind,
                    Detail = kind == DiffKind.Unchanged
                        ? "unchanged"
                        : $"sha {HashUtil.Normalize(a.sha256)} → {HashUtil.Normalize(b.sha256)}"
                });
            }
        }

        static void CompareMaterials(AssetDiffReport report, MaterialsManifest installed, MaterialsManifest candidate)
        {
            var leftSlots = (installed?.slots ?? new List<MaterialSlot>())
                .ToDictionary(s => s.slot_id ?? string.Empty, s => s, StringComparer.Ordinal);
            var rightSlots = (candidate?.slots ?? new List<MaterialSlot>())
                .ToDictionary(s => s.slot_id ?? string.Empty, s => s, StringComparer.Ordinal);

            foreach (var slotId in leftSlots.Keys.Union(rightSlots.Keys).OrderBy(s => s, StringComparer.Ordinal))
            {
                leftSlots.TryGetValue(slotId, out var a);
                rightSlots.TryGetValue(slotId, out var b);
                if (a == null)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "material",
                        Path = slotId,
                        Kind = DiffKind.Added,
                        Detail = $"material_id={b.material_id}"
                    });
                }
                else if (b == null)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "material",
                        Path = slotId,
                        Kind = DiffKind.Removed,
                        Detail = $"material_id={a.material_id}"
                    });
                }
                else if (!string.Equals(a.material_id, b.material_id, StringComparison.Ordinal))
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "material",
                        Path = slotId,
                        Kind = DiffKind.Changed,
                        Detail = $"{a.material_id} → {b.material_id}"
                    });
                }
                else
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "material",
                        Path = slotId,
                        Kind = DiffKind.Unchanged,
                        Detail = a.material_id
                    });
                }
            }

            var leftMats = (installed?.materials ?? new List<MaterialDefinition>())
                .ToDictionary(m => m.material_id ?? string.Empty, m => m, StringComparer.Ordinal);
            var rightMats = (candidate?.materials ?? new List<MaterialDefinition>())
                .ToDictionary(m => m.material_id ?? string.Empty, m => m, StringComparer.Ordinal);

            foreach (var id in leftMats.Keys.Union(rightMats.Keys).OrderBy(s => s, StringComparer.Ordinal))
            {
                leftMats.TryGetValue(id, out var a);
                rightMats.TryGetValue(id, out var b);
                if (a == null || b == null)
                {
                    continue;
                }

                var leftTex = SerializeTextures(a);
                var rightTex = SerializeTextures(b);
                if (!string.Equals(leftTex, rightTex, StringComparison.Ordinal))
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "texture",
                        Path = id,
                        Kind = DiffKind.Changed,
                        Detail = "texture bindings changed"
                    });
                }
            }
        }

        static void CompareHierarchy(AssetDiffReport report, GameObject installed, GameObject candidate)
        {
            var left = CollectHierarchy(installed);
            var right = CollectHierarchy(candidate);
            foreach (var path in left.Union(right).OrderBy(p => p, StringComparer.Ordinal))
            {
                var inLeft = left.Contains(path);
                var inRight = right.Contains(path);
                if (inLeft && inRight)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "hierarchy",
                        Path = path,
                        Kind = DiffKind.Unchanged,
                        Detail = "present"
                    });
                }
                else if (inRight)
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "hierarchy",
                        Path = path,
                        Kind = DiffKind.Added,
                        Detail = "new node"
                    });
                    report.HierarchyCompatible = false;
                }
                else
                {
                    report.Entries.Add(new DiffEntry
                    {
                        Category = "hierarchy",
                        Path = path,
                        Kind = DiffKind.Removed,
                        Detail = "missing in candidate"
                    });
                    report.HierarchyCompatible = false;
                }
            }
        }

        static void CompareBoundsAndPivot(AssetDiffReport report, GameObject installed, GameObject candidate)
        {
            report.InstalledBoundsSize = MeasureBoundsSize(installed);
            report.CandidateBoundsSize = MeasureBoundsSize(candidate);
            report.InstalledPivot = installed != null ? installed.transform.position : Vector3.zero;
            report.CandidatePivot = candidate != null ? candidate.transform.position : Vector3.zero;

            if ((report.InstalledBoundsSize - report.CandidateBoundsSize).sqrMagnitude > 1e-6f)
            {
                report.Entries.Add(new DiffEntry
                {
                    Category = "bounds",
                    Path = "renderer_bounds",
                    Kind = DiffKind.Changed,
                    Detail = $"{report.InstalledBoundsSize} → {report.CandidateBoundsSize}"
                });
            }

            if ((report.InstalledPivot - report.CandidatePivot).sqrMagnitude > 1e-6f)
            {
                report.Entries.Add(new DiffEntry
                {
                    Category = "pivot",
                    Path = "root_pivot",
                    Kind = DiffKind.Changed,
                    Detail = $"{report.InstalledPivot} → {report.CandidatePivot}"
                });
            }
        }

        static HashSet<string> CollectHierarchy(GameObject root)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (root == null)
            {
                return set;
            }

            Collect(root.transform, string.Empty, set);
            return set;
        }

        static void Collect(Transform t, string prefix, HashSet<string> set)
        {
            var path = string.IsNullOrEmpty(prefix) ? t.name : prefix + "/" + t.name;
            set.Add(path);
            for (var i = 0; i < t.childCount; i++)
            {
                Collect(t.GetChild(i), path, set);
            }
        }

        static Vector3 MeasureBoundsSize(GameObject root)
        {
            if (root == null)
            {
                return Vector3.zero;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.size;
        }

        static GameObject LoadHierarchy(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return prefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        static string SerializeTextures(MaterialDefinition definition)
        {
            if (definition?.textures == null)
            {
                return string.Empty;
            }

            return string.Join("|", definition.textures.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        }

        static string RoleCategory(string role)
        {
            if (string.Equals(role, "model", StringComparison.OrdinalIgnoreCase))
            {
                return "geometry";
            }

            if (string.Equals(role, "texture", StringComparison.OrdinalIgnoreCase))
            {
                return "texture";
            }

            return string.IsNullOrEmpty(role) ? "file" : role.ToLowerInvariant();
        }

        static string NormalizePath(string path) =>
            (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
    }
}
