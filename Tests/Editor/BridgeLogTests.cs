using NUnit.Framework;
using Unslop.UnityBridge.Editor.Diagnostics;

namespace Unslop.UnityBridge.Editor.Tests
{
    public sealed class BridgeLogTests
    {
        [Test]
        public void RedactsBearerAndSignedUrls()
        {
            var input = "Authorization Bearer abcdefghijklmnop url=https://cdn.example/file?X-Amz-Signature=deadbeef";
            var redacted = BridgeLog.Redact(input);
            Assert.That(redacted, Does.Contain("[REDACTED]"));
            Assert.That(redacted, Does.Not.Contain("abcdefghijklmnop"));
            Assert.That(redacted, Does.Not.Contain("deadbeef"));
        }
    }
}
