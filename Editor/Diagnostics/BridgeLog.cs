using System;
using System.Text.RegularExpressions;
using UDebug = UnityEngine.Debug;

namespace Unslop.UnityBridge.Editor.Diagnostics
{
    public static class BridgeLog
    {
        static readonly Regex SecretPattern = new Regex(
            @"(usk_[A-Za-z0-9]+)|(Bearer\s+[A-Za-z0-9\-._~+/]+=*)|(https?://[^\s""']*(X-Amz-|Signature=|token=)[^\s""']*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Redact(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            return SecretPattern.Replace(message, "[REDACTED]");
        }

        public static void Info(string message)
        {
            var redacted = Redact(message);
            UDebug.Log($"[Unslop] {redacted}");
            BridgeDebugMode.Append("INFO", redacted);
        }

        public static void Warn(string message)
        {
            var redacted = Redact(message);
            UDebug.LogWarning($"[Unslop] {redacted}");
            BridgeDebugMode.Append("WARN", redacted);
        }

        public static void Error(string message)
        {
            var redacted = Redact(message);
            UDebug.LogError($"[Unslop] {redacted}");
            BridgeDebugMode.Append("ERROR", redacted);
        }

        /// <summary>
        /// Verbose diagnostics — Console + session file only when <see cref="BridgeDebugMode"/> is on.
        /// </summary>
        public static void Debug(string message)
        {
            if (!BridgeDebugMode.Enabled)
            {
                return;
            }

            var redacted = Redact(message);
            UDebug.Log($"[Unslop:debug] {redacted}");
            BridgeDebugMode.Append("DEBUG", redacted);
        }

        public static void Exception(Exception ex, string context = null)
        {
            var prefix = string.IsNullOrEmpty(context) ? string.Empty : context + ": ";
            var text = $"{prefix}{Redact(ex?.Message)}\n{Redact(ex?.ToString())}";
            UDebug.LogError($"[Unslop] {text}");
            BridgeDebugMode.Append("EXCEPTION", text);
        }
    }
}
