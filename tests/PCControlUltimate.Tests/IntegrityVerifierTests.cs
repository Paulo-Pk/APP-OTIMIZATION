using System;
using PCControlUltimate.Security;
using Xunit;

namespace PCControlUltimate.Tests;

public class IntegrityVerifierTests
{
    [Fact]
    public void ComputeHmacSha256_RejectsMissingSecretKey()
    {
        Assert.Throws<ArgumentException>(() => IntegrityVerifier.ComputeHmacSha256(string.Empty, "payload"));
        Assert.Throws<ArgumentException>(() => IntegrityVerifier.ComputeHmacSha256("   ", "payload"));
    }

    [Fact]
    public void VerifyHmacSha256_ReturnsFalseWhenSecretKeyIsMissing()
    {
        Assert.False(IntegrityVerifier.VerifyHmacSha256(string.Empty, "payload", new string('0', 64)));
        Assert.False(IntegrityVerifier.VerifyHmacSha256("   ", "payload", new string('0', 64)));
    }

    [Fact]
    public void HmacSha256_ValidSecretKeyProducesVerifiableSignature()
    {
        const string key = "test-only-random-looking-secret-key";
        const string payload = "PC Control integrity test";

        string signature = IntegrityVerifier.ComputeHmacSha256(key, payload);

        Assert.Equal(64, signature.Length);
        Assert.True(IntegrityVerifier.VerifyHmacSha256(key, payload, signature));
        Assert.False(IntegrityVerifier.VerifyHmacSha256(key, payload + "-tampered", signature));
    }

    [Fact]
    public void ComputeRequestHmacSha256_RejectsMissingSessionToken()
    {
        Assert.Throws<ArgumentException>(() => IntegrityVerifier.ComputeRequestHmacSha256(
            string.Empty, "POST", "/api/test", "1", "nonce", "{}"));
    }
}
