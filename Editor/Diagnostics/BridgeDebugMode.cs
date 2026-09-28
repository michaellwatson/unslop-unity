using System;
using System.IO;
using System.Text;
using Unslop.UnityBridge.Editor.Locking;
using UnityEditor;
using UnityEngine;
using UDebug = UnityEngine.Debug;

namespace Unslop.UnityBridge.Editor.Diagnostics
{
    /// <summary>
    /// Editor-local verbose logging. When enabled, BridgeLog writes a session file under
    /// Library/Unslop/Diagnostics and emits extra transform / pipeline diagnostics.
    /// Sessions can be uploaded to the bound project via <see cref="BridgeDebugUploader"/>.
    /// </summary>
    public static class BridgeDebugMode
    {
        const string PrefKey = "Unslop.DebugMode";
        const string SessionIdPrefKey = "Unslop.DebugSessionId";
        const string UploadedOffsetPrefKey = "Unslop.DebugUploadedOffset";
        const string ChunkIndexPrefKey = "Unslop.DebugChunkIndex";
        const string SessionFileName = "debug-session.log";
        static readonly object FileLock = new object();

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set
            {
                var wasEnabled = EditorPrefs.GetBool(PrefKey, false);
                EditorPrefs.SetBool(PrefKey, value);
                if (value && !wasEnabled)
                {
                    EnsureSessionId(forceNew: string.IsNullOrEmpty(CurrentSessionId));
                    AppendRaw(
                        $"===== debug session started {DateTime.UtcNow:O} " +
                        $"(Unity {Application.unityVersion}) session={CurrentSessionId} =====");
                    BridgeLog.Info(
                        "Debug logging ON — writing to " + LogFilePath +
                        " (uploads to bound project via Upload Debug Log / auto-flush).");
                }
                else if (!value && wasEnabled)
                {
                    AppendRaw($"===== debug session stopped {DateTime.UtcNow:O} =====");
                    BridgeLog.Info("Debug logging OFF.");
                }
            }
        }

        public static string LogFilePath =>
            Path.Combine(ManagedPaths.DiagnosticsDir, SessionFileName);

        public static string CurrentSessionId =>
            EditorPrefs.GetString(SessionIdPrefKey, string.Empty);

        public static long UploadedByteOffset
        {
            get => long.TryParse(EditorPrefs.GetString(UploadedOffsetPrefKey, "0"), out var n) ? n : 0L;
            set => EditorPrefs.SetString(UploadedOffsetPrefKey, Math.Max(0L, value).ToString());
        }

        public static int NextChunkIndex
        {
            get => EditorPrefs.GetInt(ChunkIndexPrefKey, 0);
            set => EditorPrefs.SetInt(ChunkIndexPrefKey, Math.Max(0, value));
        }

        public static string EnsureSessionId(bool forceNew = false)
        {
            if (!forceNew && !string.IsNullOrEmpty(CurrentSessionId))
            {
                return CurrentSessionId;
            }

            var id = Guid.NewGuid().ToString();
            EditorPrefs.SetString(SessionIdPrefKey, id);
            UploadedByteOffset = 0;
            NextChunkIndex = 0;
            return id;
        }

        public static void Append(string level, string message)
        {
            if (!Enabled)
            {
                return;
            }

            AppendRaw($"{DateTime.UtcNow:O} [{level}] {BridgeLog.Redact(message ?? string.Empty)}");
        }

        public static void ClearLog()
        {
            try
            {
                ManagedPaths.EnsureOperationalDirectories();
                EnsureSessionId(forceNew: true);
                File.WriteAllText(
                    LogFilePath,
                    $"===== debug log cleared {DateTime.UtcNow:O} session={CurrentSessionId} =====\n",
                    Encoding.UTF8);
                UploadedByteOffset = 0;
                NextChunkIndex = 0;
            }
            catch (Exception ex)
            {
                UDebug.LogWarning("[Unslop] Could not clear debug log: " + BridgeLog.Redact(ex.Message));
            }
        }

        public static void RevealLog()
        {
            ManagedPaths.EnsureOperationalDirectories();
            if (!File.Exists(LogFilePath))
            {
                ClearLog();
            }

            EditorUtility.RevealInFinder(LogFilePath);
        }

        /// <summary>
        /// Returns unread log bytes since the last successful API upload, advancing no cursor.
        /// </summary>
        public static string ReadPendingContent(out long startOffset, out long endOffset)
        {
            startOffset = UploadedByteOffset;
            endOffset = startOffset;
            if (!File.Exists(LogFilePath))
            {
                return string.Empty;
            }

            lock (FileLock)
            {
                using (var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (startOffset > stream.Length)
                    {
                        startOffset = 0;
                    }

                    stream.Seek(startOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true))
                    {
                        var pending = reader.ReadToEnd();
                        endOffset = stream.Position;
                        return pending ?? string.Empty;
                    }
                }
            }
        }

        public static void MarkUploaded(long endOffset, int chunksAdvanced)
        {
            UploadedByteOffset = endOffset;
            NextChunkIndex += Math.Max(0, chunksAdvanced);
        }

        static void AppendRaw(string line)
        {
            try
            {
                ManagedPaths.EnsureOperationalDirectories();
                if (string.IsNullOrEmpty(CurrentSessionId))
                {
                    EnsureSessionId();
                }

                lock (FileLock)
                {
                    File.AppendAllText(LogFilePath, line + "\n", Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                UDebug.LogWarning("[Unslop] Debug log write failed: " + BridgeLog.Redact(ex.Message));
            }
        }
    }
}
