using System.IO;
using Unslop.UnityBridge.Editor.Locking;

namespace Unslop.UnityBridge.Editor.Transactions
{
    public readonly struct RecoveryScanResult
    {
        public bool Attempted { get; }
        public string Summary { get; }

        public RecoveryScanResult(bool attempted, string summary)
        {
            Attempted = attempted;
            Summary = summary ?? string.Empty;
        }
    }

    /// <summary>
    /// Scans Library/Unslop/Transactions for incomplete journals on Editor load.
    /// Full recovery is implemented with the transaction coordinator (Phase C).
    /// </summary>
    public static class TransactionRecovery
    {
        public static RecoveryScanResult TryRecoverIncompleteJournals()
        {
            ManagedPaths.EnsureOperationalDirectories();
            if (!Directory.Exists(ManagedPaths.TransactionsDir))
            {
                return new RecoveryScanResult(false, "no transaction directory");
            }

            var incomplete = 0;
            foreach (var dir in Directory.GetDirectories(ManagedPaths.TransactionsDir))
            {
                var journal = Path.Combine(dir, "journal.json");
                if (!File.Exists(journal))
                {
                    continue;
                }

                var text = File.ReadAllText(journal);
                if (text.Contains("\"committed\": true") || text.Contains("\"status\": \"committed\""))
                {
                    continue;
                }

                incomplete++;
            }

            if (incomplete == 0)
            {
                return new RecoveryScanResult(false, "no incomplete journals");
            }

            return new RecoveryScanResult(true, $"{incomplete} incomplete journal(s) pending recovery");
        }
    }
}
