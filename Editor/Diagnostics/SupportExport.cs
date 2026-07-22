using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Unslop.UnityBridge.Editor.Bootstrap;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Settings;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Diagnostics
{
    public static class SupportExport
    {
        public static string ExportRedactedDiagnostics(string reason = null)
        {
            ManagedPaths.EnsureOperationalDirectories();
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var exportRoot = Path.Combine(ManagedPaths.DiagnosticsDir, "export_" + stamp);
            Directory.CreateDirectory(exportRoot);

            var settings = UnslopProjectSettings.EnsureExists();
            File.WriteAllText(
                Path.Combine(exportRoot, "readme.txt"),
                "Unslop Unity Bridge diagnostic export (redacted).\n"
                + "unity=" + Application.unityVersion + "\n"
                + "bridge=" + PackageInfo.Version + "\n"
                + "project=" + settings.BoundProjectId + "\n"
                + "reason=" + (reason ?? string.Empty) + "\n",
                Encoding.UTF8);

            if (File.Exists(ManagedPaths.LockFilePath))
            {
                File.WriteAllText(
                    Path.Combine(exportRoot, "Unslop.lock.json"),
                    BridgeLog.Redact(File.ReadAllText(ManagedPaths.LockFilePath)),
                    Encoding.UTF8);
            }

            var drift = DriftDiagnostics.Scan();
            var driftText = string.Join(
                "\n",
                drift.Select(f => $"{f.AssetId}|{f.Code}|{BridgeLog.Redact(f.Message)}"));
            File.WriteAllText(Path.Combine(exportRoot, "drift.txt"), driftText + "\n", Encoding.UTF8);

            if (Directory.Exists(ManagedPaths.TransactionsDir))
            {
                var txnOut = Path.Combine(exportRoot, "transactions");
                Directory.CreateDirectory(txnOut);
                foreach (var dir in Directory.GetDirectories(ManagedPaths.TransactionsDir))
                {
                    var journal = Path.Combine(dir, "journal.json");
                    if (!File.Exists(journal))
                    {
                        continue;
                    }

                    File.WriteAllText(
                        Path.Combine(txnOut, Path.GetFileName(dir) + ".journal.json"),
                        BridgeLog.Redact(File.ReadAllText(journal)),
                        Encoding.UTF8);
                }
            }

            var zipPath = exportRoot + ".zip";
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(exportRoot, zipPath);
            BridgeLog.Info("Support export written to " + zipPath);
            return zipPath;
        }

        // Compatibility alias used by older call sites.
        public static string ExportRedactedBundle() => ExportRedactedDiagnostics();
    }
}
