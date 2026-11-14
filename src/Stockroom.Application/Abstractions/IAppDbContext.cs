using Microsoft.EntityFrameworkCore;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;
using Stockroom.Domain.Topology;
using Stockroom.Domain.Users;

namespace Stockroom.Application.Abstractions;

/// <summary>
/// Unit of work over the WMS tables. EF Core already implements repository + unit of work,
/// so the application layer uses DbSets directly instead of wrapping them in per-entity repositories.
/// </summary>
public interface IAppDbContext
{
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Zone> Zones { get; }
    DbSet<Location> Locations { get; }
    DbSet<Product> Products { get; }
    DbSet<Lot> Lots { get; }
    DbSet<StockItem> StockItems { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Asn> Asns { get; }
    DbSet<PutAwayTask> PutAwayTasks { get; }
    DbSet<Order> Orders { get; }
    DbSet<PickTask> PickTasks { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ICurrentUser
{
    string UserName { get; }
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken Create(User user);
}
