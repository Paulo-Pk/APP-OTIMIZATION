using PCControlUltimate.Windows.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class RegistryOptimizerTests
{
    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Control\PriorityControl")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games")]
    public void AllowedSubkeys_ShouldContainCoreGamingOptimizationPaths(string subkey)
    {
        Assert.Contains(subkey, RegistryOptimizer.AllowedSubkeys);
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows Defender")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\WindowsFirewall")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce")]
    public void DangerousOrSecurityPaths_MustNeverBeInWhitelist(string dangerousPath)
    {
        Assert.DoesNotContain(dangerousPath, RegistryOptimizer.AllowedSubkeys);
    }

    [Fact]
    public void TryApplyDword_WithForbiddenPath_ShouldFailWithoutModifyingSystem()
    {
        bool success = RegistryOptimizer.TryApplyDword(
            @"SOFTWARE\MaliciousKey\Payload",
            "DisruptiveValue",
            1,
            out var previousValue
        );

        Assert.False(success);
        Assert.Null(previousValue);
    }
}
