using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Application.Common;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Topology;

namespace Stockroom.Application.MasterData;

public sealed record CreateProductRequest(string Sku, string Barcode, string Name, decimal UnitVolume, AbcClass AbcClass);

public sealed record ProductDto(int Id, string Sku, string Barcode, string Name, decimal UnitVolume, AbcClass AbcClass);

public sealed record LocationDto(
    int Id, string Code, string ZoneCode, ZoneType ZoneType, AbcClass AbcClass, int X, int Y, decimal MaxVolume, bool IsBlocked);

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(32).Matches("^[0-9A-Za-z]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.UnitVolume).GreaterThan(0).LessThanOrEqualTo(10_000);
        RuleFor(x => x.AbcClass).IsInEnum();
    }
}

public sealed class MasterDataService(IAppDbContext db)
{
    public async Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product(request.Sku, request.Barcode, request.Name, request.UnitVolume, request.AbcClass);
        if (await db.Products.AnyAsync(p => p.Sku == product.Sku || p.Barcode == product.Barcode, ct))
        {
            throw new ConflictException($"A product with SKU {product.Sku} or barcode {product.Barcode} already exists.");
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return ToDto(product);
    }

    public Task<PagedResult<ProductDto>> ListProductsAsync(string? search, PageQuery page, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Sku.Contains(term) || p.Name.Contains(term) || p.Barcode == term);
        }

        return query.OrderBy(p => p.Sku)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Barcode, p.Name, p.UnitVolume, p.AbcClass))
            .ToPagedResultAsync(page, ct);
    }

    public Task<PagedResult<LocationDto>> ListLocationsAsync(string? zoneCode, PageQuery page, CancellationToken ct)
    {
        var query = db.Locations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(zoneCode))
        {
            var zone = zoneCode.Trim().ToUpperInvariant();
            query = query.Where(l => l.Zone.Code == zone);
        }

        return query.OrderBy(l => l.Code)
            .Select(l => new LocationDto(l.Id, l.Code, l.Zone.Code, l.Zone.Type, l.Zone.AbcClass, l.X, l.Y, l.MaxVolume, l.IsBlocked))
            .ToPagedResultAsync(page, ct);
    }

    public async Task<LocationDto> SetBlockedAsync(string code, bool blocked, CancellationToken ct)
    {
        var location = await db.GetLocationAsync(code, ct);
        if (blocked)
        {
            location.Block();
        }
        else
        {
            location.Unblock();
        }

        await db.SaveChangesAsync(ct);
        return new LocationDto(location.Id, location.Code, location.Zone.Code, location.Zone.Type, location.Zone.AbcClass,
            location.X, location.Y, location.MaxVolume, location.IsBlocked);
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Sku, p.Barcode, p.Name, p.UnitVolume, p.AbcClass);
}
