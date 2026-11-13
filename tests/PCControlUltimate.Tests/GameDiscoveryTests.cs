using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
        string appId = "987" + (Convert.ToUInt64(Guid.NewGuid().ToString("N")[..12], 16) % 10_000_000).ToString("D7");
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

    [Theory]
    [InlineData(RegistryView.Registry32)]
    [InlineData(RegistryView.Registry64)]
    public void DiscoverInstalledGames_ReadsUbisoftConnectMachineCatalog(RegistryView registryView)
    {
        string entryName = $"PCControlUbisoft_{Guid.NewGuid():N}";
        string installRoot = Path.Combine(Path.GetTempPath(), entryName);
        string catalogPath = $@"Software\Ubisoft\Launcher\Installs\{entryName}";
        Directory.CreateDirectory(installRoot);

        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
            using (RegistryKey entry = baseKey.CreateSubKey(catalogPath)!)
            {
                entry.SetValue("InstallDir", installRoot, RegistryValueKind.String);
                entry.SetValue("DisplayName", "PC Control Ubisoft Test", RegistryValueKind.String);
            }

            var game = Assert.Single(GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(item => item.Launcher == "Ubisoft Connect" && item.AppId == entryName));

            Assert.Equal("PC Control Ubisoft Test", game.Title);
            Assert.Equal(Path.GetFullPath(installRoot), game.InstallLocation);
            Assert.True(game.IsInstalled);
        }
        finally
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                baseKey.DeleteSubKeyTree(catalogPath, throwOnMissingSubKey: false);
            }
            catch { }

            try { Directory.Delete(installRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(RegistryView.Registry32)]
    [InlineData(RegistryView.Registry64)]
    public void DiscoverInstalledGames_ReadsEaAppMachineCatalog(RegistryView registryView)
    {
        string entryName = $"PCControlEa_{Guid.NewGuid():N}";
        string installRoot = Path.Combine(Path.GetTempPath(), entryName);
        string catalogPath = $@"Software\EA Games\{entryName}";
        Directory.CreateDirectory(installRoot);

        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
            using (RegistryKey entry = baseKey.CreateSubKey(catalogPath)!)
            {
                entry.SetValue("Install Dir", installRoot, RegistryValueKind.String);
                entry.SetValue("DisplayName", "PC Control EA Test", RegistryValueKind.String);
            }

            var game = Assert.Single(GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(item => item.Launcher == "EA app" && item.AppId == entryName));

            Assert.Equal("PC Control EA Test", game.Title);
            Assert.Equal(Path.GetFullPath(installRoot), game.InstallLocation);
            Assert.True(game.IsInstalled);
        }
        finally
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                baseKey.DeleteSubKeyTree(catalogPath, throwOnMissingSubKey: false);
            }
            catch { }

            try { Directory.Delete(installRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiscoverInstalledGames_ReadsEpicManifestAndValidatesLaunchExecutable()
    {
        string manifestsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        string appName = $"PCControlEpic_{Guid.NewGuid():N}";
        string manifestPath = Path.Combine(manifestsRoot, $"{appName}.item");
        string installRoot = Path.Combine(Path.GetTempPath(), appName);
        string executablePath = Path.Combine(installRoot, "Binaries", "Win64", "game.exe");

        Directory.CreateDirectory(manifestsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "test");
        File.WriteAllText(manifestPath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                DisplayName = "PC Control Epic Test",
                InstallLocation = installRoot,
                AppName = appName,
                LaunchExecutable = Path.Combine("Binaries", "Win64", "game.exe")
            }));

        try
        {
            var game = Assert.Single(GameNetworkAcceleratorService.DiscoverInstalledGames()
                .Where(item => item.Launcher == "Epic Games" && item.AppId == appName));

            Assert.Equal("PC Control Epic Test", game.Title);
            Assert.Equal(Path.GetFullPath(installRoot), game.InstallLocation);
            Assert.Equal(Path.GetFullPath(executablePath), game.ExecutablePath);
            Assert.True(game.IsInstalled);
        }
        finally
        {
            try { File.Delete(manifestPath); } catch { }
            try { Directory.Delete(installRoot, recursive: true); } catch { }

            // Remove only empty directories created for this test; preserve launcher data.
            try { if (Directory.Exists(manifestsRoot) && !Directory.EnumerateFileSystemEntries(manifestsRoot).Any()) Directory.Delete(manifestsRoot); } catch { }
            try
            {
                string dataRoot = Directory.GetParent(manifestsRoot)!.FullName;
                if (Directory.Exists(dataRoot) && !Directory.EnumerateFileSystemEntries(dataRoot).Any()) Directory.Delete(dataRoot);
            }
            catch { }
            try
            {
                string launcherRoot = Directory.GetParent(Directory.GetParent(manifestsRoot)!.FullName)!.FullName;
                if (Directory.Exists(launcherRoot) && !Directory.EnumerateFileSystemEntries(launcherRoot).Any()) Directory.Delete(launcherRoot);
            }
            catch { }
            try
            {
                string epicRoot = Directory.GetParent(Directory.GetParent(Directory.GetParent(manifestsRoot)!.FullName)!.FullName)!.FullName;
                if (Directory.Exists(epicRoot) && !Directory.EnumerateFileSystemEntries(epicRoot).Any()) Directory.Delete(epicRoot);
            }
            catch { }
        }
    }

    [Fact]
    public void TryReadMicrosoftGameConfig_IdentifiesGameAndRejectsExecutableOutsidePackage()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-xbox-test-{Guid.NewGuid():N}");
        string installRoot = Path.Combine(testRoot, "Package");
        string executablePath = Path.Combine(installRoot, "Game", "Game.exe");
        string outsideExecutable = Path.Combine(testRoot, "outside.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "test");
        File.WriteAllText(outsideExecutable, "test");

        MethodInfo parser = typeof(GameNetworkAcceleratorService).GetMethod(
            "TryReadMicrosoftGameConfig", BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "MicrosoftGame.config"),
                """
                <Game configVersion="1">
                  <Identity Name="Publisher.TestGame" Publisher="CN=Test" Version="1.0.0.0" />
                  <ExecutableList>
                    <Executable Name="Game\\Game.exe" Id="Game" />
                  </ExecutableList>
                  <ShellVisuals DefaultDisplayName="PC Control Xbox Test" />
                </Game>
                """);

            object?[] arguments = { installRoot, "Package_TestGame", null };
            Assert.True((bool)parser.Invoke(null, arguments)!);
            var game = Assert.IsType<InstalledGameEntry>(arguments[2]);
            Assert.Equal("PC Control Xbox Test", game.Title);
            Assert.Equal("Publisher.TestGame", game.AppId);
            Assert.Equal("Xbox / Microsoft Store", game.Launcher);
            Assert.Equal(Path.GetFullPath(executablePath), game.ExecutablePath);
            Assert.True(game.IsInstalled);

            File.WriteAllText(Path.Combine(installRoot, "MicrosoftGame.config"),
                """
                <Game configVersion="1">
                  <Identity Name="Publisher.TestGame" />
                  <ExecutableList>
                    <Executable Name="../outside.exe" Id="Game" />
                  </ExecutableList>
                  <ShellVisuals DefaultDisplayName="PC Control Xbox Test" />
                </Game>
                """);

            object?[] traversalArguments = { installRoot, "Package_TestGame", null };
            Assert.True((bool)parser.Invoke(null, traversalArguments)!);
            var traversalGame = Assert.IsType<InstalledGameEntry>(traversalArguments[2]);
            Assert.Null(traversalGame.ExecutablePath);
        }
        finally
        {
            try { Directory.Delete(testRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DiscoverXboxGamesFolder_FindsOnlyImmediateGdkGameFolders()
    {
        string xboxGamesRoot = Path.Combine(Path.GetTempPath(), $"pccontrol-xboxgames-{Guid.NewGuid():N}");
        string gameRoot = Path.Combine(xboxGamesRoot, "InstalledGame");
        string nestedRoot = Path.Combine(xboxGamesRoot, "OrdinaryFolder", "NestedGame");
        Directory.CreateDirectory(gameRoot);
        Directory.CreateDirectory(nestedRoot);
        Directory.CreateDirectory(Path.Combine(xboxGamesRoot, "OrdinaryFolder"));

        const string gameConfig = """
            <Game configVersion="1">
              <Identity Name="Publisher.XboxFolderTest" Publisher="CN=Test" Version="1.0.0.0" />
              <ShellVisuals DefaultDisplayName="PC Control Xbox Folder Test" />
            </Game>
            """;
        File.WriteAllText(Path.Combine(gameRoot, "MicrosoftGame.config"), gameConfig);
        File.WriteAllText(Path.Combine(nestedRoot, "MicrosoftGame.config"), """
            <Game configVersion="1">
              <Identity Name="Publisher.NestedShouldNotAppear" />
              <ShellVisuals DefaultDisplayName="Nested Game" />
            </Game>
            """);

        MethodInfo discover = typeof(GameNetworkAcceleratorService).GetMethod(
            "DiscoverXboxGamesFolder", BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            var games = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<InstalledGameEntry>>(
                discover.Invoke(null, new object[] { xboxGamesRoot }));

            var game = Assert.Single(games);
            Assert.Equal("PC Control Xbox Folder Test", game.Title);
            Assert.Equal("Publisher.XboxFolderTest", game.AppId);
            Assert.Equal("Xbox / Microsoft Store", game.Launcher);
            Assert.Equal(Path.GetFullPath(gameRoot), game.InstallLocation);
            Assert.True(game.IsInstalled);
        }
        finally
        {
            try { Directory.Delete(xboxGamesRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("Forza Horizon 6", false)]
    [InlineData("Game Name - Expansion Pack", true)]
    [InlineData("Game Name Season Pass", true)]
    [InlineData("Game Name Original Soundtrack", true)]
    [InlineData("Game Name Dedicated Server", true)]
    [InlineData("Game Name DLC", true)]
    [InlineData("Game Name - Add-on Pack", true)]
    [InlineData("Game Name - Expansions", true)]
    [InlineData("Game Name Workshop Tools", true)]
    [InlineData("Game Name Dedicated Server Tools", true)]
    [InlineData("Microsoft Visual C++ Redistributable", true)]
    [InlineData("Steam Linux Runtime", true)]
    public void GameCatalogFilter_ExcludesAddonsButKeepsBaseGames(string title, bool expectedExcluded)
    {
        MethodInfo filter = typeof(GameNetworkAcceleratorService).GetMethod(
            "IsDlcOrNonGameCatalogEntry", BindingFlags.NonPublic | BindingFlags.Static)!;

        bool excluded = (bool)filter.Invoke(null, new object[] { title })!;
        Assert.Equal(expectedExcluded, excluded);
    }


    [Theory]
    [InlineData("PC Control Game DLC")]
    [InlineData("PC Control Game Expansion Pack")]
    [InlineData("PC Control Game Season Pass")]
    [InlineData("PC Control Game Soundtrack")]
    [InlineData("PC Control Game - Character Pack")]
    public void MainGameCatalogFilter_ExcludesDlcAndStandaloneAddOnEntries(string title)
    {
        MethodInfo filter = typeof(GameNetworkAcceleratorService).GetMethod(
            "IsMainGameTitle", BindingFlags.NonPublic | BindingFlags.Static)!;

        bool isMainGame = (bool)filter.Invoke(null, new object?[] { title })!;

        Assert.False(isMainGame);
    }

    [Theory]
    [InlineData("Counter-Strike 2")]
    [InlineData("Forza Horizon 6")]
    [InlineData("Cyberpunk 2077 Ultimate Edition")]
    public void MainGameCatalogFilter_KeepsBaseGameAndNormalEditions(string title)
    {
        MethodInfo filter = typeof(GameNetworkAcceleratorService).GetMethod(
            "IsMainGameTitle", BindingFlags.NonPublic | BindingFlags.Static)!;

        bool isMainGame = (bool)filter.Invoke(null, new object?[] { title })!;

        Assert.True(isMainGame);
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
