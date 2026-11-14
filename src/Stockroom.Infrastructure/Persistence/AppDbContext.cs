using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;
using Stockroom.Domain.Topology;
using Stockroom.Domain.Users;

namespace Stockroom.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Asn> Asns => Set<Asn>();
    public DbSet<PutAwayTask> PutAwayTasks => Set<PutAwayTask>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PickTask> PickTasks => Set<PickTask>();
    public DbSet<User> Users => Set<User>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // The ledger is append-only: corrections are new Adjustment movements, never edits.
        if (ChangeTracker.Entries<StockMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Stock movements are immutable.");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Nothing in the WMS is hard-deleted; Restrict also avoids SQL Server's "multiple cascade paths" error.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<decimal>().HavePrecision(12, 3);

        // Enums as readable strings: the DB is queried by people, not only by this app.
        configurationBuilder.Properties<ZoneType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<AbcClass>().HaveConversion<string>().HaveMaxLength(1);
        configurationBuilder.Properties<StockMovementType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<AsnStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<PutAwayTaskStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<PickTaskStatus>().HaveConversion<string>().HaveMaxLength(16);
    }
}
