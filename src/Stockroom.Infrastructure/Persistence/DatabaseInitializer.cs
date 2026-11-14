using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Stockroom.Infrastructure.Persistence;

/// <summary>Applies migrations and, optionally, demo data. Meant for Development and tests; production runs a migration bundle.</summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services, bool seedDemoData, IReadOnlyList<DemoUser> demoUsers, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);
        if (seedDemoData)
        {
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(demoUsers, ct);
        }
    }
}
