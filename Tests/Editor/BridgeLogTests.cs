using NUnit.Framework;
using Unslop.UnityBridge.Editor.Diagnostics;

namespace Unslop.UnityBridge.Editor.Tests
{
    public sealed class BridgeLogTests
    {
        [Test]
        public void Redact_RemovesShortApiKeyPrefix()
        {
            const string keyFragment = "usk_testkey";
            var redacted = BridgeLog.Redact("Saving key " + keyFragment);
            Assert.That(redacted, Does.Contain("[REDACTED]"));
            Assert.That(redacted, Does.Not.Contain(keyFragment));
        }

        [Test]
        public void Redact_RemovesBearerAndSignedUrls()
        {
            var input = "Authorization Bearer abcdefghijklmnop url=https://cdn.example/file?X-Amz-Signature=deadbeef";
            var redacted = BridgeLog.Redact(input);
            Assert.That(redacted, Does.Contain("[REDACTED]"));
            Assert.That(redacted, Does.Not.Contain("abcdefghijklmnop"));
            Assert.That(redacted, Does.Not.Contain("deadbeef"));
        }

        [Test]
        public void Redact_LeavesOrdinaryMessages()
        {
            const string msg = "Bound project Latch (c64b0622-074d-440b-96b7-fc604d2caec6).";
            Assert.That(BridgeLog.Redact(msg), Is.EqualTo(msg));
        }
    }
}
