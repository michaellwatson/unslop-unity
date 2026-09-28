using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unslop.UnityBridge.Editor.Api;
using Unslop.UnityBridge.Editor.Locking;
using Unslop.UnityBridge.Editor.Services;
using Unslop.UnityBridge.Editor.Settings;

namespace Unslop.UnityBridge.Editor.Diagnostics
{
    /// <summary>
    /// Uploads / downloads Bridge debug sessions via project-scoped diagnostics endpoints.
    /// </summary>
    public static class BridgeDebugUploader
    {
        public const int MaxChunkBytes = 512 * 1024;

        public static async Task<DiagnosticUploadResultDto> FlushAsync(
            IUnslopApiClient api = null,
            string projectId = null,
            string assetId = null,
            bool isFinal = false,
            CancellationToken cancellationToken = default)
        {
            api ??= BridgeServices.CreateApiClient();
            projectId = string.IsNullOrWhiteSpace(projectId)
                ? UnslopProjectSettings.EnsureExists().BoundProjectId
                : projectId;

            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new InvalidOperationException("Bind a project before uploading diagnostics.");
            }

            var sessionId = BridgeDebugMode.EnsureSessionId();
            var pending = BridgeDebugMode.ReadPendingContent(out _, out var endOffset);
            if (string.IsNullOrEmpty(pending))
            {
                BridgeLog.Info("No new debug log content to upload.");
                return null;
            }

            DiagnosticUploadResultDto last = null;
            var offset = 0;
            var chunks = 0;
            var chunkIndex = BridgeDebugMode.NextChunkIndex;
            var utf8 = Encoding.UTF8;

            while (offset < pending.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = pending.Length - offset;
                var take = remaining;
                if (utf8.GetByteCount(pending.Substring(offset, take)) > MaxChunkBytes)
                {
                    take = FitChunk(pending, offset, MaxChunkBytes);
                }

                var slice = pending.Substring(offset, take);
                var isLastSlice = offset + take >= pending.Length;
                var request = new DiagnosticUploadRequestDto
                {
                    session_id = sessionId,
                    engine = "unity",
                    kind = "debug_session",
                    content = slice,
                    chunk_index = chunkIndex,
                    is_final = isFinal && isLastSlice,
                    asset_id = string.IsNullOrWhiteSpace(assetId) ? null : assetId,
                    client = BridgeServices.CreateClientContext()
                };

                var idempotency = $"{sessionId}:{chunkIndex}";
                last = await api.UploadProjectDiagnosticsAsync(
                    projectId,
                    request,
                    idempotency,
                    cancellationToken).ConfigureAwait(false);

                offset += take;
                chunkIndex++;
                chunks++;
            }

            BridgeDebugMode.MarkUploaded(endOffset, chunks);
            BridgeLog.Info(
                $"Uploaded debug session {sessionId} to project {projectId} " +
                $"({chunks} chunk(s), bytes={last?.byte_length ?? 0}).");
            return last;
        }

        /// <summary>
        /// Lists project diagnostics and downloads the newest session content to a local file.
        /// </summary>
        public static async Task<string> PullLatestAsync(
            IUnslopApiClient api = null,
            string projectId = null,
            string engine = "unity",
            CancellationToken cancellationToken = default)
        {
            api ??= BridgeServices.CreateApiClient();
            projectId = string.IsNullOrWhiteSpace(projectId)
                ? UnslopProjectSettings.EnsureExists().BoundProjectId
                : projectId;

            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new InvalidOperationException("Bind a project before pulling diagnostics.");
            }

            var page = await api.ListProjectDiagnosticsAsync(
                projectId,
                engine,
                kind: "debug_session",
                limit: 1,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var newest = page?.data?.FirstOrDefault();
            if (newest == null || string.IsNullOrWhiteSpace(newest.session_id))
            {
                throw new InvalidOperationException("No diagnostic sessions found for this project.");
            }

            var detail = await api.GetProjectDiagnosticAsync(
                projectId,
                newest.session_id,
                includeContent: true,
                cancellationToken).ConfigureAwait(false);

            ManagedPaths.EnsureOperationalDirectories();
            var shortId = newest.session_id.Length > 8 ? newest.session_id.Substring(0, 8) : newest.session_id;
            var path = Path.Combine(ManagedPaths.DiagnosticsDir, $"pulled-{shortId}.log");
            File.WriteAllText(path, detail?.content ?? string.Empty, Encoding.UTF8);
            BridgeLog.Info(
                $"Pulled debug session {newest.session_id} from project {projectId} " +
                $"({detail?.byte_length ?? 0} bytes) → {path}");
            return path;
        }

        public static async void TryFlushFireAndForget(string assetId = null, bool isFinal = false)
        {
            if (!BridgeDebugMode.Enabled)
            {
                return;
            }

            try
            {
                await FlushAsync(assetId: assetId, isFinal: isFinal).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("Debug log upload failed: " + BridgeLog.Redact(ex.Message));
            }
        }

        static int FitChunk(string text, int start, int maxBytes)
        {
            var utf8 = Encoding.UTF8;
            var low = 1;
            var high = text.Length - start;
            var best = 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var bytes = utf8.GetByteCount(text.Substring(start, mid));
                if (bytes <= maxBytes)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return Math.Max(1, best);
        }
    }
}
