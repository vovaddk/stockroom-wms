using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application.Common;
using Stockroom.Application.MasterData;

namespace Stockroom.Api.Endpoints;

internal static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Master data").RequireAuthorization(Policies.Operator);

        group.MapGet("/products", (string? search, [AsParameters] PageQuery page, MasterDataService service, CancellationToken ct) =>
            service.ListProductsAsync(search, page, ct));

        group.MapPost("/products", async (CreateProductRequest request, MasterDataService service, CancellationToken ct) =>
            {
                var product = await service.CreateProductAsync(request, ct);
                return TypedResults.Created($"/api/products?search={product.Sku}", product);
            })
            .RequireAuthorization(Policies.Manager)
            .Validate<CreateProductRequest>();

        group.MapGet("/locations", (string? zone, [AsParameters] PageQuery page, MasterDataService service, CancellationToken ct) =>
            service.ListLocationsAsync(zone, page, ct));

        group.MapPost("/locations/{code}/block", (string code, MasterDataService service, CancellationToken ct) =>
                service.SetBlockedAsync(code, blocked: true, ct))
            .RequireAuthorization(Policies.Manager);

        group.MapPost("/locations/{code}/unblock", (string code, MasterDataService service, CancellationToken ct) =>
                service.SetBlockedAsync(code, blocked: false, ct))
            .RequireAuthorization(Policies.Manager);
    }
}
