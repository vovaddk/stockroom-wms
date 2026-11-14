using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Topology;

namespace Stockroom.Application.Common;

/// <summary>Lookups shared by the inbound, outbound and inventory use cases.</summary>
internal static class StockQueries
{
    public static async Task<Location> GetLocationAsync(this IAppDbContext db, string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Locations.Include(l => l.Zone).FirstOrDefaultAsync(l => l.Code == normalized, ct)
            ?? throw new NotFoundException($"Location {normalized} does not exist.");
    }

    public static async Task<Product> GetProductByBarcodeAsync(this IAppDbContext db, string barcode, CancellationToken ct)
    {
        var normalized = barcode.Trim();
        return await db.Products.FirstOrDefaultAsync(p => p.Barcode == normalized, ct)
            ?? throw new NotFoundException($"No product with barcode {normalized}.");
    }

    public static async Task<Lot> GetLotAsync(this IAppDbContext db, Product product, string lotNumber, CancellationToken ct)
    {
        var normalized = lotNumber.Trim().ToUpperInvariant();
        return await db.Lots.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.LotNumber == normalized, ct)
            ?? throw new NotFoundException($"Lot {normalized} of {product.Sku} does not exist.");
    }

    /// <summary>Returns the projection row for a lot in a bin, creating (and tracking) an empty one if needed.</summary>
    public static async Task<StockItem> GetOrAddStockItemAsync(this IAppDbContext db, Location location, Lot lot, CancellationToken ct)
    {
        var stock = lot.Id == 0
            ? null
            : await db.StockItems.FirstOrDefaultAsync(s => s.LocationId == location.Id && s.LotId == lot.Id, ct);

        if (stock is null)
        {
            stock = new StockItem(location, lot);
            db.StockItems.Add(stock);
        }

        return stock;
    }

    public static Task<decimal> GetUsedVolumeAsync(this IAppDbContext db, Location location, CancellationToken ct) =>
        db.StockItems.Where(s => s.LocationId == location.Id).SumAsync(s => s.Quantity * s.Product.UnitVolume, ct);
}
