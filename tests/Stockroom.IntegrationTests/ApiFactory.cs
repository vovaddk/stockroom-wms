using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Application.Auth;
using Stockroom.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(Stockroom.IntegrationTests.ApiFactory))]

namespace Stockroom.IntegrationTests;

/// <summary>
/// Hosts the real API against a real SQL Server database that is unique per test run and dropped afterwards.
/// Server comes from STOCKROOM_TEST_SQL (CI) or defaults to the local SQL Express instance.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("STOCKROOM_TEST_SQL")
        ?? @"Server=localhost\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True")
    {
        InitialCatalog = $"StockroomWms_Tests_{Guid.NewGuid():N}",
    }.ConnectionString;

    public ValueTask InitializeAsync()
    {
        _ = Server; // starts the host: migrations + demo users/data run on startup
        return ValueTask.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    public async Task<HttpClient> CreateClientAsAsync(string userName)
    {
        var client = CreateClient();
        var password = userName == "manager" ? "Manager#2026" : "Operator#2026";
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development settings provide the demo users, demo JWT key and migrate/seed on startup.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
    }
}
