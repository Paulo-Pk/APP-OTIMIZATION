using Microsoft.EntityFrameworkCore;
using PCControlUltimate.Core.Entities;

namespace PCControlUltimate.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<OptimizationAction> OptimizationActions => Set<OptimizationAction>();
    public DbSet<NetworkRouteDiagnosticRecord> NetworkRouteDiagnostics => Set<NetworkRouteDiagnosticRecord>();
    public DbSet<RelayNodeEntity> RelayNodes => Set<RelayNodeEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OptimizationAction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Category).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PreviousValue).HasMaxLength(256);
            entity.Property(e => e.TargetValue).HasMaxLength(256);
        });

        modelBuilder.Entity<NetworkRouteDiagnosticRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GameProcessName).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TargetEndpoint).IsRequired().HasMaxLength(256);
            entity.Property(e => e.RecommendedRoute).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RecommendationReason).HasMaxLength(512);
        });

        modelBuilder.Entity<RelayNodeEntity>(entity =>
        {
            entity.HasKey(e => e.NodeId);
            entity.Property(e => e.NodeId).HasMaxLength(64);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(160);
            entity.Property(e => e.RegionCode).IsRequired().HasMaxLength(32);
            entity.Property(e => e.EndpointHost).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PublicKey).HasMaxLength(128);
            entity.Property(e => e.AllowedTargetCidrs).HasMaxLength(1024);
        });
    }
}
