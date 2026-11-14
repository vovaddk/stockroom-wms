using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;
using Stockroom.Domain.Topology;
using Stockroom.Domain.Users;

namespace Stockroom.Infrastructure.Persistence.Configurations;

internal static class ConcurrencyToken
{
    public const string Name = "RowVersion";

    /// <summary>SQL Server <c>rowversion</c> as a shadow property: optimistic concurrency without leaking into the domain model.</summary>
    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class => builder.Property<byte[]>(Name).IsRowVersion().IsRequired();
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.Property(x => x.Code).HasMaxLength(16);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasMany(x => x.Zones).WithOne(z => z.Warehouse).HasForeignKey(z => z.WarehouseId);
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> b)
    {
        b.Property(x => x.Code).HasMaxLength(16);
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasMany(x => x.Locations).WithOne(l => l.Zone).HasForeignKey(l => l.ZoneId);
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Sku).HasMaxLength(32);
        b.Property(x => x.Barcode).HasMaxLength(32);
        b.HasIndex(x => x.Sku).IsUnique();
        b.HasIndex(x => x.Barcode).IsUnique();
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> b)
    {
        b.Property(x => x.LotNumber).HasMaxLength(32);
        b.HasIndex(x => new { x.ProductId, x.LotNumber }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
    }
}

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> b)
    {
        b.ToTable(t =>
        {
            // Defence in depth: the domain already forbids negative stock, the database refuses it too.
            t.HasCheckConstraint("CK_StockItems_Quantity", "[Quantity] >= 0");
            t.HasCheckConstraint("CK_StockItems_Reserved", "[ReservedQuantity] >= 0 AND [ReservedQuantity] <= [Quantity]");
        });
        b.HasRowVersion();
        b.HasIndex(x => new { x.LocationId, x.LotId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable(t => t.HasCheckConstraint("CK_StockMovements_Quantity", "[Quantity] > 0"));
        b.Property(x => x.UserName).HasMaxLength(64);
        b.Property(x => x.Reference).HasMaxLength(64);
        b.HasIndex(x => new { x.ProductId, x.OccurredAt });
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId);
        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId);
        b.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId);
    }
}

internal sealed class AsnConfiguration : IEntityTypeConfiguration<Asn>
{
    public void Configure(EntityTypeBuilder<Asn> b)
    {
        b.Property(x => x.Number).HasMaxLength(32);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.AsnId);
    }
}

internal sealed class AsnLineConfiguration : IEntityTypeConfiguration<AsnLine>
{
    public void Configure(EntityTypeBuilder<AsnLine> b)
    {
        b.HasRowVersion();
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
    }
}

internal sealed class PutAwayTaskConfiguration : IEntityTypeConfiguration<PutAwayTask>
{
    public void Configure(EntityTypeBuilder<PutAwayTask> b)
    {
        b.HasRowVersion();
        b.Property(x => x.CompletedBy).HasMaxLength(64);
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId);
        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId);
        b.HasOne(x => x.SuggestedLocation).WithMany().HasForeignKey(x => x.SuggestedLocationId);
        b.HasOne(x => x.ActualLocation).WithMany().HasForeignKey(x => x.ActualLocationId);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasRowVersion();
        b.Property(x => x.Number).HasMaxLength(32);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId);
        b.HasMany(x => x.PickTasks).WithOne(t => t.Order).HasForeignKey(t => t.OrderId);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> b) =>
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
}

internal sealed class PickTaskConfiguration : IEntityTypeConfiguration<PickTask>
{
    public void Configure(EntityTypeBuilder<PickTask> b)
    {
        b.HasRowVersion();
        b.Property(x => x.CompletedBy).HasMaxLength(64);
        b.HasIndex(x => new { x.Status, x.OrderId, x.Sequence });
        b.HasOne(x => x.OrderLine).WithMany().HasForeignKey(x => x.OrderLineId);
        b.HasOne(x => x.StockItem).WithMany().HasForeignKey(x => x.StockItemId);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.UserName).HasMaxLength(64);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.Role).HasMaxLength(16);
        b.HasIndex(x => x.UserName).IsUnique();
    }
}
