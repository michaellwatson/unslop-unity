using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Unslop.UnityBridge.Editor.Locking;

namespace Unslop.UnityBridge.Editor.Transactions
{
    public static class TransactionPhases
    {
        public const string Prepared = "prepared";
        public const string Snapshotted = "snapshotted";
        public const string Applied = "applied";
        public const string Imported = "imported";
        public const string Regenerated = "regenerated";
        public const string Verified = "verified";
        public const string Committed = "committed";
        public const string Discarded = "discarded";
        public const string Failed = "failed";
        public const string Recovering = "recovering";
    }

    [Serializable]
    public sealed class TransactionJournalRecord
    {
        public int schema_version = 1;
        public string transaction_id = string.Empty;
        public string asset_id = string.Empty;
        public string operation = "update";
        public string from_version_id = string.Empty;
        public string to_version_id = string.Empty;
        public string status = "open";
        public string phase = TransactionPhases.Prepared;
        public bool committed;
        public string created_at = string.Empty;
        public string updated_at = string.Empty;
        public string snapshot_dir = string.Empty;
        public string staging_path = string.Empty;
        public string lock_entry_json = string.Empty;
        public string local_metadata_json = string.Empty;
        public string wrapper_prefab_guid = string.Empty;
        public string visual_prefab_guid = string.Empty;
        public string error = string.Empty;
        public string import_profile_hash = string.Empty;
        public string manifest_sha256 = string.Empty;
    }

    /// <summary>
    /// Persists transition journals under Library/Unslop/Transactions/&lt;id&gt;/journal.json.
    /// </summary>
    public static class TransactionJournal
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string JournalDirectory(string transactionId) =>
            Path.Combine(ManagedPaths.TransactionsDir, ManagedPaths.SanitizeId(transactionId));

        public static string JournalPath(string transactionId) =>
            Path.Combine(JournalDirectory(transactionId), "journal.json");

        public static TransactionJournalRecord Create(
            string assetId,
            string operation,
            string fromVersionId,
            string toVersionId)
        {
            ManagedPaths.EnsureOperationalDirectories();
            var id = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow.ToString("o");
            var record = new TransactionJournalRecord
            {
                transaction_id = id,
                asset_id = assetId ?? string.Empty,
                operation = operation ?? "update",
                from_version_id = fromVersionId ?? string.Empty,
                to_version_id = toVersionId ?? string.Empty,
                status = "open",
                phase = TransactionPhases.Prepared,
                committed = false,
                created_at = now,
                updated_at = now,
                snapshot_dir = Path.Combine(JournalDirectory(id), "snapshot")
            };
            Save(record);
            return record;
        }

        public static TransactionJournalRecord Load(string transactionId)
        {
            var path = JournalPath(transactionId);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<TransactionJournalRecord>(
                File.ReadAllText(path, Encoding.UTF8),
                Settings);
        }

        public static void Save(TransactionJournalRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            record.updated_at = DateTime.UtcNow.ToString("o");
            var dir = JournalDirectory(record.transaction_id);
            Directory.CreateDirectory(dir);
            var path = JournalPath(record.transaction_id);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(record, Settings), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, path + ".bak");
            }
            else
            {
                File.Move(temp, path);
            }
        }

        public static void Advance(TransactionJournalRecord record, string phase, string status = null)
        {
            record.phase = phase;
            if (!string.IsNullOrEmpty(status))
            {
                record.status = status;
            }

            if (phase == TransactionPhases.Committed)
            {
                record.committed = true;
                record.status = "committed";
            }

            if (phase == TransactionPhases.Discarded)
            {
                record.committed = false;
                record.status = "discarded";
            }

            Save(record);
        }

        public static void Fail(TransactionJournalRecord record, Exception ex)
        {
            record.phase = TransactionPhases.Failed;
            record.status = "failed";
            record.error = ex?.Message ?? "unknown error";
            Save(record);
        }

        public static bool IsTerminal(TransactionJournalRecord record)
        {
            if (record == null)
            {
                return true;
            }

            return record.committed
                   || record.phase == TransactionPhases.Committed
                   || record.phase == TransactionPhases.Discarded
                   || string.Equals(record.status, "committed", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(record.status, "discarded", StringComparison.OrdinalIgnoreCase);
        }
    }
}
