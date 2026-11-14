using Microsoft.AspNetCore.Http.HttpResults;
using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application.Common;
using Stockroom.Application.Outbound;
using Stockroom.Domain.Outbound;

namespace Stockroom.Api.Endpoints;

internal static class OutboundEndpoints
{
    public static void MapOutboundEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Outbound").RequireAuthorization(Policies.Operator);

        orders.MapGet("/", (OrderStatus? status, [AsParameters] PageQuery page, OutboundService service, CancellationToken ct) =>
            service.ListOrdersAsync(status, page, ct));

        orders.MapGet("/{id:int}", (int id, OutboundService service, CancellationToken ct) => service.GetOrderAsync(id, ct));

        orders.MapPost("/", async (CreateOrderRequest request, OutboundService service, CancellationToken ct) =>
            {
                var order = await service.CreateOrderAsync(request, ct);
                return TypedResults.Created($"/api/orders/{order.Id}", order);
            })
            .RequireAuthorization(Policies.Manager)
            .Validate<CreateOrderRequest>();

        orders.MapPost("/{id:int}/allocate", (int id, OutboundService service, CancellationToken ct) => service.AllocateAsync(id, ct))
            .RequireAuthorization(Policies.Manager);

        orders.MapPost("/{id:int}/ship", (int id, OutboundService service, CancellationToken ct) => service.ShipAsync(id, ct));

        var picks = app.MapGroup("/api/pick-tasks").WithTags("Outbound").RequireAuthorization(Policies.Operator);

        picks.MapGet("/next", async Task<Results<Ok<PickTaskDto>, NoContent>> (int? orderId, OutboundService service, CancellationToken ct) =>
            await service.GetNextPickTaskAsync(orderId, ct) is { } task ? TypedResults.Ok(task) : TypedResults.NoContent());

        picks.MapPost("/{id:int}/confirm", (int id, ConfirmPickRequest request, OutboundService service, CancellationToken ct) =>
                service.ConfirmPickAsync(id, request, ct))
            .Validate<ConfirmPickRequest>();
    }
}
