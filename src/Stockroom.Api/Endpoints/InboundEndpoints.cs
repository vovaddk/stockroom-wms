using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application.Common;
using Stockroom.Application.Inbound;
using Stockroom.Domain.Inbound;

namespace Stockroom.Api.Endpoints;

internal static class InboundEndpoints
{
    public static void MapInboundEndpoints(this IEndpointRouteBuilder app)
    {
        var asns = app.MapGroup("/api/asns").WithTags("Inbound").RequireAuthorization(Policies.Operator);

        asns.MapGet("/", (AsnStatus? status, [AsParameters] PageQuery page, InboundService service, CancellationToken ct) =>
            service.ListAsnsAsync(status, page, ct));

        asns.MapGet("/{id:int}", (int id, InboundService service, CancellationToken ct) => service.GetAsnAsync(id, ct));

        asns.MapPost("/", async (CreateAsnRequest request, InboundService service, CancellationToken ct) =>
            {
                var asn = await service.CreateAsnAsync(request, ct);
                return TypedResults.Created($"/api/asns/{asn.Id}", asn);
            })
            .RequireAuthorization(Policies.Manager)
            .Validate<CreateAsnRequest>();

        asns.MapPost("/{id:int}/receipts", (int id, ReceiveRequest request, InboundService service, CancellationToken ct) =>
                service.ReceiveAsync(id, request, ct))
            .Validate<ReceiveRequest>();

        var tasks = app.MapGroup("/api/putaway-tasks").WithTags("Inbound").RequireAuthorization(Policies.Operator);

        tasks.MapGet("/", (PutAwayTaskStatus? status, [AsParameters] PageQuery page, InboundService service, CancellationToken ct) =>
            service.ListPutAwayTasksAsync(status, page, ct));

        tasks.MapPost("/{id:int}/confirm", (int id, ConfirmPutAwayRequest request, InboundService service, CancellationToken ct) =>
                service.ConfirmPutAwayAsync(id, request, ct))
            .Validate<ConfirmPutAwayRequest>();
    }
}
