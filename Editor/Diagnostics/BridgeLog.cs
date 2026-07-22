using System;
using System.Text.RegularExpressions;
using UnityEngine;

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

        public static void Info(string message) => Debug.Log($"[Unslop] {Redact(message)}");
        public static void Warn(string message) => Debug.LogWarning($"[Unslop] {Redact(message)}");
        public static void Error(string message) => Debug.LogError($"[Unslop] {Redact(message)}");
        public static void Exception(Exception ex, string context = null)
        {
            var prefix = string.IsNullOrEmpty(context) ? string.Empty : context + ": ";
            Debug.LogError($"[Unslop] {prefix}{Redact(ex?.Message)}\n{Redact(ex?.ToString())}");
        }
    }
}
