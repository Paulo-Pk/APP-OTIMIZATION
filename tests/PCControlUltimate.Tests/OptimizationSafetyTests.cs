using System;
using System.IO;
using PCControlUltimate.Diagnostics;
using PCControlUltimate.Security;
using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class OptimizationSafetyTests
{
    [Fact]
    public void EvaluateThermalState_WhenSensorUnavailable_ShouldReturnSensorUnavailableWithoutFalseValues()
    {
        var resultZero = GpuThermalAuditor.EvaluateThermalState(0.0f, 0.0f);
        Assert.Equal(GpuThermalHealth.SensorUnavailable, resultZero.Status);
        Assert.Equal(0.0f, resultZero.Delta);

        var resultNaN = GpuThermalAuditor.EvaluateThermalState(float.NaN, 75.0f);
        Assert.Equal(GpuThermalHealth.SensorUnavailable, resultNaN.Status);
    }

    [Fact]
    public void EvaluateThermalState_WhenDeltaIsNormal_ShouldReturnHealthy()
    {
        float coreTemp = 60.0f;
        float hotspotTemp = 74.0f; // Delta = 14°C < 18°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Excellent, result.Status);
        Assert.True(result.Delta <= 18.0f);
    }

    [Fact]
    public void EvaluateThermalState_WhenDeltaIsHigh_ShouldWarnContextually()
    {
        float coreTemp = 65.0f;
        float hotspotTemp = 93.0f; // Delta = 28°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Warning, result.Status);
        Assert.Contains("contextualizar", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateBearerToken_WithValidSessionToken_ReturnsTrue()
    {
        using var server = new PrivilegedAgentServer(19876);
        string validHeader = $"Bearer {server.SessionToken}";

        Assert.True(server.ValidateBearerToken(validHeader));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong-token-value-1234567890")]
    public void ValidateBearerToken_WithMissingOrInvalidToken_ReturnsFalse(string? authHeader)
    {
        using var server = new PrivilegedAgentServer(19877);
        Assert.False(server.ValidateBearerToken(authHeader));
    }

    [Theory]
    [InlineData("127.0.0.1:5005", true)]
    [InlineData("localhost:5005", true)]
    [InlineData("evil.attacker.com:5005", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateHostHeader_EnforcesStrictLoopbackHost(string? hostHeader, bool expectedValid)
    {
        using var server = new PrivilegedAgentServer(19878);
        Assert.Equal(expectedValid, server.ValidateHostHeader(hostHeader));
    }

    [Fact]
    public void ValidateOriginAndClientPolicy_EnforcesOriginAndLocalClientAntiReplay()
    {
        using var server = new PrivilegedAgentServer(19879);
        string nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // 1. Authorized localhost Origin with anti-replay nonce succeeds
        var validOriginResult = server.ValidateOriginAndClientPolicy(
            "http://localhost:3000",
            "PCControl-WebBridge/2.1",
            nowUnix,
            $"nonce-web-{Guid.NewGuid():N}"
        );
        Assert.True(validOriginResult.IsValid);

        // 2. Unauthorized external Origin must be rejected
        var evilOriginResult = server.ValidateOriginAndClientPolicy(
            "https://evil-site.example.com",
            "PCControl-WebBridge/2.1",
            nowUnix,
            $"nonce-evil-{Guid.NewGuid():N}"
        );
        Assert.False(evilOriginResult.IsValid);

        // 3. Missing Origin WITHOUT X-PCControl-Client header must be rejected
        var missingClientHeader = server.ValidateOriginAndClientPolicy(
            null,
            null,
            nowUnix,
            $"nonce-missing-client-{Guid.NewGuid():N}"
        );
        Assert.False(missingClientHeader.IsValid);

        // 4. Legitimate WPF client without Origin but WITH X-PCControl-Client + valid timestamp + unique nonce succeeds
        string wpfNonce = $"nonce-wpf-{Guid.NewGuid():N}";
        var validWpfClient = server.ValidateOriginAndClientPolicy(
            null,
            "PCControl-WPF/2.1",
            nowUnix,
            wpfNonce
        );
        Assert.True(validWpfClient.IsValid);

        // 5. Replaying the exact same nonce must be rejected
        var replayAttempt = server.ValidateOriginAndClientPolicy(
            null,
            "PCControl-WPF/2.1",
            nowUnix,
            wpfNonce
        );
        Assert.False(replayAttempt.IsValid);
        Assert.Contains("Replay", replayAttempt.ErrorReason, StringComparison.OrdinalIgnoreCase);

        // 6. Expired timestamp outside the 120s window must be rejected
        string expiredUnix = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var expiredAttempt = server.ValidateOriginAndClientPolicy(
            null,
            "PCControl-WPF/2.1",
            expiredUnix,
            $"nonce-expired-{Guid.NewGuid():N}"
        );
        Assert.False(expiredAttempt.IsValid);
    }

    [Fact]
    public void DpapiSessionTokenProtection_SaveAndReadRoundtrip_VerifiesProtectionReport()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pccontrol_dpapi_test_{Guid.NewGuid():N}.bin");
        try
        {
            string expectedToken = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            var report = IntegrityVerifier.SaveProtectedSessionTokenWithReport(tempFile, expectedToken);
            Assert.True(report.Success);
            Assert.True(File.Exists(tempFile));

            string? restoredToken = IntegrityVerifier.ReadProtectedSessionToken(tempFile);
            Assert.Equal(expectedToken, restoredToken);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}