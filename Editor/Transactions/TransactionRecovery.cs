using System.IO;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Diagnostics;
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
    /// Scans Library/Unslop/Transactions for incomplete journals on Editor load
    /// and delegates recovery to <see cref="AssetTransitionCoordinator"/>.
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
                if (text.Contains("\"committed\": true")
                    || text.Contains("\"status\": \"committed\"")
                    || text.Contains("\"status\": \"discarded\"")
                    || text.Contains("\"phase\": \"committed\"")
                    || text.Contains("\"phase\": \"discarded\""))
                {
                    continue;
                }

                incomplete++;
            }

            if (incomplete == 0)
            {
                return new RecoveryScanResult(false, "no incomplete journals");
            }

            var recovered = 0;
            try
            {
                var coordinator = new AssetTransitionCoordinator();
                // Synchronous wait is acceptable on Editor delayCall bootstrap path.
                recovered = coordinator.RecoverAsync().GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                BridgeLog.Exception(ex, "Transaction recovery");
                return new RecoveryScanResult(true, $"{incomplete} incomplete journal(s); recovery error: {BridgeLog.Redact(ex.Message)}");
            }

            return new RecoveryScanResult(
                true,
                $"{incomplete} incomplete journal(s); recovered={recovered}");
        }

        public static Task<int> RecoverAsync() => new AssetTransitionCoordinator().RecoverAsync();
    }
}
