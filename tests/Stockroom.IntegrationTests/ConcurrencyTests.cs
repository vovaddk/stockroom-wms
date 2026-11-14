using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Application.Outbound;
using Stockroom.Infrastructure.Persistence;

namespace Stockroom.IntegrationTests;

public class ConcurrencyTests(ApiFactory factory)
{
    [Fact]
    public async Task Two_pickers_confirming_the_same_task_one_wins_the_other_gets_409()
    {
        var manager = await factory.CreateClientAsAsync("manager");
        var s = new Scenario(manager, await factory.CreateClientAsAsync("operator"));
        var bin = await s.StockedProductAsync(10);
        var task = (await s.AllocatedOrderAsync(4)).PickTasks.Single();
        var picker1 = await factory.CreateClientAsAsync("operator");
        var picker2 = await factory.CreateClientAsAsync("operator");
        var confirm = new ConfirmPickRequest(bin, s.Barcode, 4);

        var responses = await Task.WhenAll(
            picker1.PostAsJsonAsync($"/api/pick-tasks/{task.Id}/confirm", confirm),
            picker2.PostAsJsonAsync($"/api/pick-tasks/{task.Id}/confirm", confirm));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var stock = await s.GetPageAsync<Application.Reports.StockDto>(manager, $"/api/stock?sku={s.Sku}");
        Assert.Equal(6, stock.Items.Single().Quantity);
    }

    [Fact]
    public async Task Stale_stock_row_cannot_be_saved_over_a_newer_version()
    {
        var s = new Scenario(await factory.CreateClientAsAsync("manager"), await factory.CreateClientAsAsync("operator"));
        await s.StockedProductAsync(10);

        await using var scope1 = factory.Services.CreateAsyncScope();
        await using var scope2 = factory.Services.CreateAsyncScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var copy1 = await db1.StockItems.Include(x => x.Product).Include(x => x.Location).SingleAsync(x => x.Product.Sku == s.Sku && x.Quantity > 0);
        var copy2 = await db2.StockItems.Include(x => x.Product).Include(x => x.Location).SingleAsync(x => x.Product.Sku == s.Sku && x.Quantity > 0);

        copy1.Reserve(6);
        await db1.SaveChangesAsync();
        copy2.Reserve(6); // valid against its stale snapshot, but would over-reserve in reality

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_rejects_negative_stock_even_if_domain_rules_were_bypassed()
    {
        var s = new Scenario(await factory.CreateClientAsAsync("manager"), await factory.CreateClientAsAsync("operator"));
        await s.StockedProductAsync(2);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE s SET Quantity = -1 FROM StockItems s JOIN Products p ON p.Id = s.ProductId WHERE p.Sku = {s.Sku}"));
        Assert.Contains("CK_StockItems", ex.Message);
    }
}
