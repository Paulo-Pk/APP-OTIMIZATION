using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PCControlUltimate.Core.Entities;

namespace PCControlUltimate.Infrastructure.Data;

public class DatabaseService
{
    private readonly string _databasePath;
    private static bool _sqliteProviderInitialized;
    private static readonly object _initLock = new();

    public string DatabasePath => _databasePath;

    public DatabaseService(string? customDatabasePath = null)
    {
        EnsureSqliteProviderInitialized();

        if (!string.IsNullOrWhiteSpace(customDatabasePath))
        {
            _databasePath = customDatabasePath;
        }
        else
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCControlUltimate",
                "Data"
            );
            Directory.CreateDirectory(appData);
            _databasePath = Path.Combine(appData, "pccontrol_ultimate.db");
        }
    }

    public static void EnsureSqliteProviderInitialized()
    {
        if (_sqliteProviderInitialized) return;
        lock (_initLock)
        {
            if (_sqliteProviderInitialized) return;
            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch
            {
                // Microsoft.Data.Sqlite also initializes the provider on connection open
            }
            _sqliteProviderInitialized = true;
        }
    }

    public AppDbContext CreateDbContext()
    {
        string? dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        return new AppDbContext(optionsBuilder.Options);
    }

    public async Task<bool> InitializeAndVerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        bool canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect;
    }

    public async Task<OptimizationAction> UpsertOptimizationActionAsync(
        string tweakId,
        string name,
        string category,
        string registryKey,
        string valueName,
        string previousValue,
        string targetValue,
        bool isApplied,
        string? backupId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var existing = await db.OptimizationActions
            .FirstOrDefaultAsync(x => x.Name == tweakId || (x.RegistryKey == registryKey && x.ValueName == valueName), cancellationToken);

        if (existing == null)
        {
            existing = new OptimizationAction
            {
                Id = Guid.NewGuid(),
                Name = tweakId,
                Category = category,
                Description = name,
                RegistryKey = registryKey,
                ValueName = valueName,
                PreviousValue = previousValue ?? string.Empty,
                TargetValue = targetValue ?? string.Empty,
                IsApplied = isApplied,
                IsReversible = true,
                AppliedAtUtc = isApplied ? DateTime.UtcNow : null,
                BackupIdentifier = backupId
            };
            db.OptimizationActions.Add(existing);
        }
        else
        {
            existing.PreviousValue = previousValue ?? existing.PreviousValue;
            existing.TargetValue = targetValue ?? existing.TargetValue;
            existing.IsApplied = isApplied;
            existing.AppliedAtUtc = isApplied ? DateTime.UtcNow : existing.AppliedAtUtc;
            existing.BackupIdentifier = backupId ?? existing.BackupIdentifier;
            db.OptimizationActions.Update(existing);
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<IReadOnlyList<OptimizationAction>> GetOptimizationActionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        return await db.OptimizationActions
            .OrderByDescending(x => x.AppliedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<NetworkRouteDiagnosticRecord> RecordRouteDiagnosticAsync(
        NetworkRouteDiagnosticRecord record,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        db.NetworkRouteDiagnostics.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<NetworkRouteDiagnosticRecord>> GetRecentRouteDiagnosticsAsync(
        int maxCount = 25,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        return await db.NetworkRouteDiagnostics
            .OrderByDescending(x => x.MeasuredAtUtc)
            .Take(Math.Clamp(maxCount, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public async Task<RelayNodeEntity> UpsertRelayNodeAsync(
        RelayNodeEntity node,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var existing = await db.RelayNodes.FirstOrDefaultAsync(x => x.NodeId == node.NodeId, cancellationToken);
        if (existing == null)
        {
            db.RelayNodes.Add(node);
        }
        else
        {
            existing.DisplayName = node.DisplayName;
            existing.RegionCode = node.RegionCode;
            existing.EndpointHost = node.EndpointHost;
            existing.EndpointPort = node.EndpointPort;
            existing.PublicKey = node.PublicKey;
            existing.AllowedTargetCidrs = node.AllowedTargetCidrs;
            existing.IsCustomVps = node.IsCustomVps;
            db.RelayNodes.Update(existing);
        }

        await db.SaveChangesAsync(cancellationToken);
        return node;
    }

    public async Task<IReadOnlyList<RelayNodeEntity>> GetRelayNodesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        return await db.RelayNodes
            .OrderBy(x => x.RegionCode)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }
}
