using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PCControlUltimate.Core.Entities;
using PCControlUltimate.Core.Interfaces;
using PCControlUltimate.Hardware;
using PCControlUltimate.Infrastructure.Data;
using PCControlUltimate.Infrastructure.Services;
using Xunit;

namespace PCControlUltimate.Tests;

public class DatabaseAndDiTests : IDisposable
{
    private readonly string _tempDbPath;

    public DatabaseAndDiTests()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "PCControlUltimate_SqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDbPath = Path.Combine(tempDir, "test_pccontrol.db");
    }

    [Fact]
    public async Task DatabaseService_ShouldInitializeSqlite_AndPerformOptimizationActionCrud()
    {
        var dbService = new DatabaseService(_tempDbPath);

        bool initialized = await dbService.InitializeAndVerifyAsync();
        Assert.True(initialized);
        Assert.True(File.Exists(_tempDbPath));

        // 1. Insert new OptimizationAction
        var created = await dbService.UpsertOptimizationActionAsync(
            tweakId: "game-dvr-disable",
            name: "Disable GameDVR Background Recording",
            category: "Gaming",
            registryKey: @"HKCU\System\GameConfigStore",
            valueName: "GameDVR_Enabled",
            previousValue: "1",
            targetValue: "0",
            isApplied: true,
            backupId: "op-12345"
        );

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.True(created.IsApplied);
        Assert.Equal("0", created.TargetValue);

        // 2. Read back from SQLite
        var allActions = await dbService.GetOptimizationActionsAsync();
        Assert.Single(allActions);
        Assert.Equal("game-dvr-disable", allActions[0].Name);

        // 3. Update existing OptimizationAction on revert
        var updated = await dbService.UpsertOptimizationActionAsync(
            tweakId: "game-dvr-disable",
            name: "Disable GameDVR Background Recording",
            category: "Gaming",
            registryKey: @"HKCU\System\GameConfigStore",
            valueName: "GameDVR_Enabled",
            previousValue: "1",
            targetValue: "1",
            isApplied: false,
            backupId: "op-12345"
        );

        Assert.Equal(created.Id, updated.Id);
        Assert.False(updated.IsApplied);

        var actionsAfterRevert = await dbService.GetOptimizationActionsAsync();
        Assert.Single(actionsAfterRevert);
        Assert.False(actionsAfterRevert[0].IsApplied);
    }

    [Fact]
    public async Task DatabaseService_ShouldPersistAndQuery_NetworkRouteDiagnostics_AndRelayNodes()
    {
        var dbService = new DatabaseService(_tempDbPath);
        await dbService.InitializeAndVerifyAsync();

        var node = await dbService.UpsertRelayNodeAsync(new RelayNodeEntity
        {
            NodeId = "sa-gru-01",
            DisplayName = "São Paulo Relay 01",
            RegionCode = "SA-EAST-1",
            EndpointHost = "203.0.113.10",
            EndpointPort = 51820,
            PublicKey = "mF8R3p9L2xQ7vK4nZ1wY6tB5cD8jH0sG3fA9eP2uV1M=",
            AllowedTargetCidrs = "155.133.248.0/24",
            IsCustomVps = true
        });

        Assert.Equal("sa-gru-01", node.NodeId);

        var savedNodes = await dbService.GetRelayNodesAsync();
        Assert.Single(savedNodes);
        Assert.Equal("203.0.113.10", savedNodes[0].EndpointHost);

        var diag = await dbService.RecordRouteDiagnosticAsync(new NetworkRouteDiagnosticRecord
        {
            GameProcessName = "cs2.exe",
            TargetEndpoint = "155.133.248.34:27015",
            DirectRttMs = 28.4,
            DirectJitterMs = 1.2,
            DirectLossPercent = 0.0,
            CandidateNodeId = "sa-gru-01",
            CandidateRttMs = 31.0,
            CandidateJitterMs = 1.8,
            CandidateLossPercent = 0.0,
            RecommendedRoute = "Direct",
            RecommendationReason = "Direct route has lower latency than relay."
        });

        Assert.NotEqual(Guid.Empty, diag.Id);

        var recentDiags = await dbService.GetRecentRouteDiagnosticsAsync(10);
        Assert.Single(recentDiags);
        Assert.Equal("Direct", recentDiags[0].RecommendedRoute);
    }

    [Fact]
    public void DependencyInjectionContainer_ShouldResolveCoreAndInfrastructureServices_WithoutCircularDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new DatabaseService(_tempDbPath));
        services.AddTransient<AppDbContext>(sp => sp.GetRequiredService<DatabaseService>().CreateDbContext());
        services.AddSingleton<IHardwareMonitorService, LibreHardwareMonitorWrapper>();
        services.AddSingleton(_ => new AgentClientService("test-session-token-1234567890"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        var dbService = provider.GetRequiredService<DatabaseService>();
        using var dbContext = provider.GetRequiredService<AppDbContext>();
        var hwMonitor = provider.GetRequiredService<IHardwareMonitorService>();
        var agentClient = provider.GetRequiredService<AgentClientService>();

        Assert.NotNull(dbService);
        Assert.NotNull(dbContext);
        Assert.NotNull(hwMonitor);
        Assert.NotNull(agentClient);
    }

    public void Dispose()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_tempDbPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Ignore file lock cleanup during test teardown
        }
    }
}
