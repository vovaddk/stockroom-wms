using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Application.Common;
using Stockroom.Domain.Inventory;

namespace Stockroom.Application.Reports;

public sealed record StockFilter(string? Sku, string? Barcode, string? LocationCode);

public sealed record StockDto(
    string LocationCode,
    string ZoneCode,
    string Sku,
    string Barcode,
    string ProductName,
    string LotNumber,
    DateOnly? ExpiryDate,
    int Quantity,
    int ReservedQuantity,
    int AvailableQuantity);

public sealed record ExpiringStockDto(
    string Sku, string ProductName, string LotNumber, DateOnly ExpiryDate, int DaysLeft, string LocationCode, int Quantity);

public sealed record MovementDto(
    long Id,
    StockMovementType Type,
    string Sku,
    string LotNumber,
    string? FromLocationCode,
    string? ToLocationCode,
    int Quantity,
    string UserName,
    string Reference,
    string? Reason,
    DateTimeOffset OccurredAt)
{
    /// <summary>Single mapping used both as SQL projection and in memory.</summary>
    public static readonly Expression<Func<StockMovement, MovementDto>> Projection = m => new MovementDto(
        m.Id, m.Type, m.Product.Sku, m.Lot.LotNumber,
        m.FromLocation != null ? m.FromLocation.Code : null,
        m.ToLocation != null ? m.ToLocation.Code : null,
        m.Quantity, m.UserName, m.Reference, m.Reason, m.OccurredAt);

    public static readonly Func<StockMovement, MovementDto> From = Projection.Compile();
}

public sealed class ReportService(IAppDbContext db, TimeProvider clock)
{
    public Task<PagedResult<StockDto>> GetStockAsync(StockFilter filter, PageQuery page, CancellationToken ct)
    {
        var query = db.StockItems.AsNoTracking().Where(s => s.Quantity > 0);
        if (!string.IsNullOrWhiteSpace(filter.Sku))
        {
            var sku = filter.Sku.Trim().ToUpperInvariant();
            query = query.Where(s => s.Product.Sku == sku);
        }

        if (!string.IsNullOrWhiteSpace(filter.Barcode))
        {
            var barcode = filter.Barcode.Trim();
            query = query.Where(s => s.Product.Barcode == barcode);
        }

        if (!string.IsNullOrWhiteSpace(filter.LocationCode))
        {
            var code = filter.LocationCode.Trim().ToUpperInvariant();
            query = query.Where(s => s.Location.Code == code);
        }

        return query
            .OrderBy(s => s.Location.Code).ThenBy(s => s.Product.Sku).ThenBy(s => s.Lot.LotNumber)
            .Select(s => new StockDto(
                s.Location.Code, s.Location.Zone.Code, s.Product.Sku, s.Product.Barcode, s.Product.Name,
                s.Lot.LotNumber, s.Lot.ExpiryDate, s.Quantity, s.ReservedQuantity, s.Quantity - s.ReservedQuantity))
            .ToPagedResultAsync(page, ct);
    }

    public async Task<PagedResult<MovementDto>> GetMovementsAsync(string sku, PageQuery page, CancellationToken ct)
    {
        var normalized = sku.Trim().ToUpperInvariant();
        if (!await db.Products.AnyAsync(p => p.Sku == normalized, ct))
        {
            throw new NotFoundException($"Product {normalized} does not exist.");
        }

        return await db.StockMovements.AsNoTracking()
            .Where(m => m.Product.Sku == normalized)
            .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
            .Select(MovementDto.Projection)
            .ToPagedResultAsync(page, ct);
    }

    /// <summary>Stock whose lot expires within <paramref name="days"/> days (already expired lots included), soonest first.</summary>
    public async Task<PagedResult<ExpiringStockDto>> GetExpiringAsync(int days, PageQuery page, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var cutoff = today.AddDays(days);

        var result = await db.StockItems.AsNoTracking()
            .Where(s => s.Quantity > 0 && s.Lot.ExpiryDate != null && s.Lot.ExpiryDate <= cutoff)
            .OrderBy(s => s.Lot.ExpiryDate).ThenBy(s => s.Location.Code)
            .Select(s => new { s.Product.Sku, s.Product.Name, s.Lot.LotNumber, Expiry = s.Lot.ExpiryDate!.Value, s.Location.Code, s.Quantity })
            .ToPagedResultAsync(page, ct);

        return result.Map(s => new ExpiringStockDto(
            s.Sku, s.Name, s.LotNumber, s.Expiry, s.Expiry.DayNumber - today.DayNumber, s.Code, s.Quantity));
    }
}
