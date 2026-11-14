using Microsoft.AspNetCore.Http.HttpResults;
using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application.Common;
using Stockroom.Application.Inventory;
using Stockroom.Application.Reports;

namespace Stockroom.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.Operator);

        api.MapPost("/transfers", (TransferRequest request, InventoryService service, CancellationToken ct) =>
                service.TransferAsync(request, ct))
            .WithTags("Inventory")
            .Validate<TransferRequest>();

        api.MapPost("/adjustments", (AdjustmentRequest request, InventoryService service, CancellationToken ct) =>
                service.AdjustAsync(request, ct))
            .WithTags("Inventory")
            .RequireAuthorization(Policies.Manager)
            .Validate<AdjustmentRequest>();

        api.MapGet("/stock", (string? sku, string? barcode, string? locationCode, [AsParameters] PageQuery page,
                ReportService reports, CancellationToken ct) =>
                reports.GetStockAsync(new StockFilter(sku, barcode, locationCode), page, ct))
            .WithTags("Reports");

        api.MapGet("/reports/movements", (string sku, [AsParameters] PageQuery page, ReportService reports, CancellationToken ct) =>
                reports.GetMovementsAsync(sku, page, ct))
            .WithTags("Reports");

        api.MapGet("/reports/expiring", async Task<Results<Ok<PagedResult<ExpiringStockDto>>, ValidationProblem>> (
                [AsParameters] PageQuery page, ReportService reports, CancellationToken ct, int days = 30) =>
                days is < 0 or > 3650
                    ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["Must be between 0 and 3650."] })
                    : TypedResults.Ok(await reports.GetExpiringAsync(days, page, ct)))
            .WithTags("Reports");
    }
}
