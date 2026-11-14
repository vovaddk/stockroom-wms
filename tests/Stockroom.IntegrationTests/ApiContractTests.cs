using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Stockroom.Application.Auth;
using Stockroom.Application.Inbound;
using Stockroom.Application.Inventory;
using Stockroom.Application.MasterData;
using Stockroom.Domain.Topology;

namespace Stockroom.IntegrationTests;

public class ApiContractTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy_database_without_auth()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_token_is_401()
    {
        var response = await factory.CreateClient().GetAsync("/api/stock");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_401_problem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("operator", "nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/adjustments")]
    [InlineData("/api/products")]
    [InlineData("/api/orders")]
    public async Task Operator_cannot_call_manager_endpoints(string url)
    {
        var op = await factory.CreateClientAsAsync("operator");

        var response = await op.PostAsJsonAsync(url, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_use_operator_endpoints()
    {
        var manager = await factory.CreateClientAsAsync("manager");

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/putaway-tasks")).StatusCode);
    }

    [Fact]
    public async Task Invalid_body_is_400_validation_problem_with_field_errors()
    {
        var manager = await factory.CreateClientAsAsync("manager");

        var response = await manager.PostAsJsonAsync("/api/products", new CreateProductRequest("", "123", "Name", -1m, AbcClass.A));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Sku", out _));
        Assert.True(errors.TryGetProperty("UnitVolume", out _));
    }

    [Fact]
    public async Task Zero_quantity_receipt_is_400()
    {
        var op = await factory.CreateClientAsAsync("operator");

        var response = await op.PostAsJsonAsync("/api/asns/1/receipts", new ReceiveRequest("4820000000011", 0, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_location_is_404_problem()
    {
        var manager = await factory.CreateClientAsAsync("manager");

        var response = await manager.PostAsJsonAsync("/api/adjustments", new AdjustmentRequest("Z-99-99-9", "4820000000011", "M-2401", 1, "Count"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_product_is_409()
    {
        var s = new Scenario(await factory.CreateClientAsAsync("manager"), await factory.CreateClientAsAsync("operator"));
        await s.CreateProductAsync();

        var manager = await factory.CreateClientAsAsync("manager");
        var response = await manager.PostAsJsonAsync("/api/products", new CreateProductRequest(s.Sku, s.Barcode, "Again", 1m, AbcClass.B));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
