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
    public void EvaluateThermalState_WhenDeltaIsNormal_ShouldReturnHealthy()
    {
        float coreTemp = 60.0f;
        float hotspotTemp = 74.0f; // Delta = 14°C < 18°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Excellent, result.Status);
        Assert.True(result.Delta <= 18.0f);
    }

    [Fact]
    public void EvaluateThermalState_WhenDeltaIsHigh_ShouldWarn()
    {
        float coreTemp = 65.0f;
        float hotspotTemp = 93.0f; // Delta = 28°C
        var result = GpuThermalAuditor.EvaluateThermalState(coreTemp, hotspotTemp);
        Assert.Equal(GpuThermalHealth.Warning, result.Status);
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

    [Fact]
    public void DpapiSessionTokenProtection_SaveAndReadRoundtrip_SucceedsForCurrentUser()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pccontrol_dpapi_test_{Guid.NewGuid():N}.bin");
        try
        {
            string expectedToken = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            bool saved = IntegrityVerifier.SaveProtectedSessionToken(tempFile, expectedToken);
            Assert.True(saved);
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