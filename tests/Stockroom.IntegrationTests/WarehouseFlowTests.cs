using System.Net;
using System.Net.Http.Json;
using Stockroom.Application.Common;
using Stockroom.Application.Inbound;
using Stockroom.Application.Outbound;
using Stockroom.Application.Reports;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;

namespace Stockroom.IntegrationTests;

public class WarehouseFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Receive_put_away_pick_and_ship_end_to_end()
    {
        var manager = await factory.CreateClientAsAsync("manager");
        var op = await factory.CreateClientAsAsync("operator");
        var s = new Scenario(manager, op);
        await s.CreateProductAsync();
        var asn = await s.CreateAsnAsync(expected: 10);

        // Two partial receipts; the second one is consolidated into the bin the first is heading to.
        var first = await (await s.ReceiveAsync(asn.Id, 6)).ReadAsync<ReceiptResult>();
        var second = await (await s.ReceiveAsync(asn.Id, 4)).ReadAsync<ReceiptResult>();
        Assert.Equal(4, first.RemainingQuantity);
        Assert.Equal(0, second.RemainingQuantity);
        Assert.NotNull(first.SuggestedLocationCode);
        Assert.Equal(first.SuggestedLocationCode, second.SuggestedLocationCode);
        Assert.Equal(AsnStatus.Closed, (await (await op.GetAsync($"/api/asns/{asn.Id}")).ReadAsync<AsnDto>()).Status);

        Assert.Equal(2, (await Scenario.OpenPutAwaysAsync(op, s.Sku)).Count);
        await s.PutAwayAsync(first);
        await s.PutAwayAsync(second);
        Assert.Empty(await Scenario.OpenPutAwaysAsync(op, s.Sku));

        var bin = first.SuggestedLocationCode!;
        var stock = await s.GetPageAsync<StockDto>(op, $"/api/stock?barcode={s.Barcode}");
        var row = Assert.Single(stock.Items);
        Assert.Equal((bin, 10, "LOT-1"), (row.LocationCode, row.Quantity, row.LotNumber));

        var order = await s.AllocatedOrderAsync(7);
        Assert.Equal(OrderStatus.Allocated, order.Status);

        var task = await (await op.GetAsync($"/api/pick-tasks/next?orderId={order.Id}")).ReadAsync<PickTaskDto>();
        Assert.Equal((bin, 7), (task.LocationCode, task.Quantity));

        var wrongBin = await op.PostAsJsonAsync($"/api/pick-tasks/{task.Id}/confirm", new ConfirmPickRequest("RCV-01", s.Barcode, 7));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongBin.StatusCode);

        var picked = await (await op.PostAsJsonAsync($"/api/pick-tasks/{task.Id}/confirm", new ConfirmPickRequest(bin, s.Barcode, 7)))
            .ReadAsync<PickConfirmation>();
        Assert.Equal((PickTaskStatus.Picked, OrderStatus.Picked), (picked.Status, picked.OrderStatus));
        Assert.Equal(HttpStatusCode.NoContent, (await op.GetAsync($"/api/pick-tasks/next?orderId={order.Id}")).StatusCode);

        var shipped = await (await op.PostAsync($"/api/orders/{order.Id}/ship", null)).ReadAsync<OrderDto>();
        Assert.Equal(OrderStatus.Shipped, shipped.Status);

        stock = await s.GetPageAsync<StockDto>(op, $"/api/stock?barcode={s.Barcode}");
        Assert.Equal(3, Assert.Single(stock.Items).Quantity);

        var history = await s.GetPageAsync<MovementDto>(op, $"/api/reports/movements?sku={s.Sku}");
        Assert.Equal(
            [StockMovementType.Pick, StockMovementType.PutAway, StockMovementType.PutAway, StockMovementType.Receipt, StockMovementType.Receipt],
            history.Items.Select(m => m.Type));
    }

    [Fact]
    public async Task Over_receipt_is_rejected_as_unprocessable_problem()
    {
        var s = new Scenario(await factory.CreateClientAsAsync("manager"), await factory.CreateClientAsAsync("operator"));
        await s.CreateProductAsync();
        var asn = await s.CreateAsnAsync(expected: 5);

        var response = await s.ReceiveAsync(asn.Id, 6);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Over-receipt", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocating_more_than_available_is_rejected_and_reserves_nothing()
    {
        var manager = await factory.CreateClientAsAsync("manager");
        var s = new Scenario(manager, await factory.CreateClientAsAsync("operator"));
        await s.StockedProductAsync(3);
        var order = await (await manager.PostAsJsonAsync("/api/orders", new CreateOrderRequest($"SO-{s.Sku}", "Customer", [new(s.Sku, 4)])))
            .ReadAsync<OrderDto>(HttpStatusCode.Created);

        var response = await manager.PostAsync($"/api/orders/{order.Id}/allocate", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var stock = await s.GetPageAsync<StockDto>(manager, $"/api/stock?sku={s.Sku}");
        Assert.Equal(0, Assert.Single(stock.Items).ReservedQuantity);
    }

    [Fact]
    public async Task Shipping_an_unpicked_order_is_a_409_state_conflict()
    {
        var manager = await factory.CreateClientAsAsync("manager");
        var s = new Scenario(manager, await factory.CreateClientAsAsync("operator"));
        await s.StockedProductAsync(5);
        var order = await s.AllocatedOrderAsync(2);

        var response = await manager.PostAsync($"/api/orders/{order.Id}/ship", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Demo_data_is_seeded_with_pick_tasks_ready()
    {
        var op = await factory.CreateClientAsAsync("operator");

        var locations = await (await op.GetAsync("/api/locations?pageSize=100")).ReadAsync<PagedResult<Application.MasterData.LocationDto>>();
        var orders = await (await op.GetAsync("/api/orders?status=Allocated")).ReadAsync<PagedResult<OrderSummaryDto>>();

        Assert.True(locations.TotalCount >= 60);
        Assert.Contains(orders.Items, o => o.Number == "SO-2002");
    }
}
