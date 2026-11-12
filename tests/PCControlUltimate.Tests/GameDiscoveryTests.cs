using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;
using PCControlUltimate.Gaming.Network;
using Xunit;

namespace PCControlUltimate.Tests;

[SupportedOSPlatform("windows")]
public sealed class GameDiscoveryTests : IDisposable
{
    private const string CatalogPath = @"Software\GOG.com\Games";
    private readonly string _registryRoot = $@"{CatalogPath}\PCControlTest_{Guid.NewGuid():N}";
    private readonly string _installRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-game-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DiscoverInstalledGames_ReadsCurrentUserGogCatalog_AndKeepsExecutablesInsideInstallFolder()
    {
        Directory.CreateDirectory(_installRoot);
        string validExecutable = Path.Combine(_installRoot, "game.exe");
        File.WriteAllText(validExecutable, "test");
        string outsideDirectory = Path.Combine(Path.GetTempPath(), $"pccontrol-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);
        string outsideExecutable = Path.Combine(outsideDirectory, "outside.exe");
        File.WriteAllText(outsideExecutable, "test");

        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
        {
            using (RegistryKey validEntry = baseKey.CreateSubKey($@"{_registryRoot}\ValidGame")!)
            {
                validEntry.SetValue("path", _installRoot);
                validEntry.SetValue("gameName", "PC Control Test Game");
                validEntry.SetValue("exe", "game.exe");
            }

            using (RegistryKey invalidEntry = baseKey.CreateSubKey($@"{_registryRoot}\ExternalExeGame")!)
            {
                invalidEntry.SetValue("path", _installRoot);
                invalidEntry.SetValue("gameName", "PC Control External Executable Test");
                invalidEntry.SetValue("exe", outsideExecutable);
            }
        }

        try
        {
            var discovered = GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(game => game.Launcher == "GOG" && game.AppId.StartsWith(_testPrefix, StringComparison.Ordinal))
                .ToList();

            var validGame = Assert.Single(discovered.Where(game => game.Title == "PC Control Test Game"));
            Assert.Equal(Path.GetFullPath(_installRoot), validGame.InstallLocation);
            Assert.Equal(Path.GetFullPath(validExecutable), validGame.ExecutablePath);

            var externalExeGame = Assert.Single(discovered.Where(game => game.Title == "PC Control External Executable Test"));
            Assert.Null(externalExeGame.ExecutablePath);
        }
        finally
        {
            try { Directory.Delete(outsideDirectory, recursive: true); } catch { }
        }
    }

    public void Dispose()
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
            baseKey.DeleteSubKeyTree(ValidEntryPath, throwOnMissingSubKey: false);
            baseKey.DeleteSubKeyTree(InvalidEntryPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            if (Directory.Exists(_installRoot))
                Directory.Delete(_installRoot, recursive: true);
        }
        catch { }
    }
}
