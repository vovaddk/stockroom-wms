using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stockroom.Application.Common;
using Stockroom.Application.Inbound;
using Stockroom.Application.MasterData;
using Stockroom.Application.Outbound;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Topology;

namespace Stockroom.IntegrationTests;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Options)!;
    }
}

/// <summary>Builds isolated test data through the public API: every test gets its own product, so tests can run in parallel.</summary>
internal sealed class Scenario(HttpClient manager, HttpClient @operator)
{
    public string Sku { get; } = $"T-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
    public string Barcode => Sku.Replace("-", "", StringComparison.Ordinal);

    public async Task<ProductDto> CreateProductAsync() =>
        await (await manager.PostAsJsonAsync("/api/products", new CreateProductRequest(Sku, Barcode, $"Test {Sku}", 1m, AbcClass.A)))
            .ReadAsync<ProductDto>(HttpStatusCode.Created);

    public async Task<AsnDto> CreateAsnAsync(int expected) =>
        await (await manager.PostAsJsonAsync("/api/asns", new CreateAsnRequest($"ASN-{Sku}", "Test supplier", [new(Sku, expected)])))
            .ReadAsync<AsnDto>(HttpStatusCode.Created);

    public Task<HttpResponseMessage> ReceiveAsync(int asnId, int quantity) =>
        @operator.PostAsJsonAsync($"/api/asns/{asnId}/receipts", new ReceiveRequest(Barcode, quantity, "LOT-1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)));

    public async Task<PutAwayTaskDto> PutAwayAsync(ReceiptResult receipt) =>
        await (await @operator.PostAsJsonAsync($"/api/putaway-tasks/{receipt.PutAwayTaskId}/confirm", new ConfirmPutAwayRequest(receipt.SuggestedLocationCode!)))
            .ReadAsync<PutAwayTaskDto>();

    /// <summary>Product with <paramref name="quantity"/> units received and put away into one storage bin.</summary>
    public async Task<string> StockedProductAsync(int quantity)
    {
        await CreateProductAsync();
        var asn = await CreateAsnAsync(quantity);
        var receipt = await (await ReceiveAsync(asn.Id, quantity)).ReadAsync<ReceiptResult>();
        var task = await PutAwayAsync(receipt);
        return task.ActualLocationCode!;
    }

    public async Task<OrderDto> AllocatedOrderAsync(int quantity, string suffix = "1")
    {
        var order = await (await manager.PostAsJsonAsync("/api/orders", new CreateOrderRequest($"SO-{Sku}-{suffix}", "Test customer", [new(Sku, quantity)])))
            .ReadAsync<OrderDto>(HttpStatusCode.Created);
        return await (await manager.PostAsync($"/api/orders/{order.Id}/allocate", null)).ReadAsync<OrderDto>();
    }

    public async Task<PagedResult<T>> GetPageAsync<T>(HttpClient client, string url) =>
        await (await client.GetAsync(url)).ReadAsync<PagedResult<T>>();

    public static async Task<IReadOnlyList<PutAwayTaskDto>> OpenPutAwaysAsync(HttpClient client, string sku)
    {
        var page = await (await client.GetAsync($"/api/putaway-tasks?status={PutAwayTaskStatus.Open}&pageSize=100")).ReadAsync<PagedResult<PutAwayTaskDto>>();
        return [.. page.Items.Where(t => t.Sku == sku)];
    }
}
