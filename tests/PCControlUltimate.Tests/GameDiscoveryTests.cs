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
    private readonly string _testPrefix = $"PCControlTest_{Guid.NewGuid():N}";
    private string ValidEntryPath => $@"{CatalogPath}\{_testPrefix}_ValidGame";
    private string InvalidEntryPath => $@"{CatalogPath}\{_testPrefix}_ExternalExeGame";
    private readonly string _installRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-game-tests-{Guid.NewGuid():N}");

    [Fact]
    public void DiscoverInstalledGames_ReadsCurrentUserGogCatalogFrom64BitRegistryView()
    {
        string installRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-game-tests-64-{Guid.NewGuid():N}");
        Directory.CreateDirectory(installRoot);

        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (RegistryKey entry = baseKey.CreateSubKey($@"{CatalogPath}\{_testPrefix}_64BitGame")!)
        {
            entry.SetValue("path", installRoot);
            entry.SetValue("gameName", "PC Control Test Game 64");
        }

        try
        {
            var game = Assert.Single(GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(item => item.Launcher == "GOG"
                    && item.AppId == $"{_testPrefix}_64BitGame"));

            Assert.Equal("PC Control Test Game 64", game.Title);
            Assert.Equal(Path.GetFullPath(installRoot), game.InstallLocation);
        }
        finally
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                baseKey.DeleteSubKeyTree($@"{CatalogPath}\{_testPrefix}_64BitGame", throwOnMissingSubKey: false);
            }
            catch { }

            try { Directory.Delete(installRoot, recursive: true); } catch { }
        }
    }

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
            using (RegistryKey validEntry = baseKey.CreateSubKey(ValidEntryPath)!)
            {
                validEntry.SetValue("path", _installRoot);
                validEntry.SetValue("gameName", "PC Control Test Game");
                validEntry.SetValue("exe", "game.exe");
            }

            using (RegistryKey invalidEntry = baseKey.CreateSubKey(InvalidEntryPath)!)
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

    [Theory]
    [InlineData(RegistryView.Registry32)]
    [InlineData(RegistryView.Registry64)]
    public void DiscoverInstalledGames_ReadsSteamRegistryRootAndAdditionalLibrary(RegistryView registryView)
    {
        string steamRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-steam-root-{Guid.NewGuid():N}");
        string libraryRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-steam-library-{Guid.NewGuid():N}");
        string appId = $"987{Guid.NewGuid():N}".Substring(0, 10);
        string installDir = $"PCControlTest_{Guid.NewGuid():N}";
        string installLocation = Path.Combine(libraryRoot, "steamapps", "common", installDir);
        const string steamKeyPath = @"Software\Valve\Steam";
        string? previousSteamPath = null;

        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(installLocation);

        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, registryView))
            using (RegistryKey steamKey = baseKey.CreateSubKey(steamKeyPath)!)
            {
                previousSteamPath = steamKey.GetValue("SteamPath") as string;
                steamKey.SetValue("SteamPath", steamRoot, RegistryValueKind.String);
            }

            string escapedLibraryPath = libraryRoot.Replace(@"\", @"\\");
            File.WriteAllText(
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                $"\"libraryfolders\"\n{{\n\t\"1\"\t{{\n\t\t\"path\"\t\"{escapedLibraryPath}\"\n\t}}\n}}\n");

            File.WriteAllText(
                Path.Combine(libraryRoot, "steamapps", $"appmanifest_{appId}.acf"),
                $"\"AppState\"\n{{\n\t\"appid\"\t\"{appId}\"\n\t\"name\"\t\"PC Control Steam Test Game\"\n\t\"installdir\"\t\"{installDir}\"\n}}\n");

            var game = Assert.Single(GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(item => item.Launcher == "Steam" && item.AppId == appId));

            Assert.Equal("PC Control Steam Test Game", game.Title);
            Assert.Equal(Path.GetFullPath(installLocation), game.InstallLocation);
            Assert.Null(game.ExecutablePath);
            Assert.True(game.IsInstalled);
        }
        finally
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, registryView);
                using RegistryKey? steamKey = baseKey.OpenSubKey(steamKeyPath, writable: true);
                if (steamKey is not null)
                {
                    if (previousSteamPath is null)
                        steamKey.DeleteValue("SteamPath", throwOnMissingValue: false);
                    else
                        steamKey.SetValue("SteamPath", previousSteamPath, RegistryValueKind.String);
                }
            }
            catch { }

            try { if (Directory.Exists(steamRoot)) Directory.Delete(steamRoot, recursive: true); } catch { }
            try { if (Directory.Exists(libraryRoot)) Directory.Delete(libraryRoot, recursive: true); } catch { }
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
