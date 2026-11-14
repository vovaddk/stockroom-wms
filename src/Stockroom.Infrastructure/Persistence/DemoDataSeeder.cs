using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;
using Stockroom.Domain.Topology;
using Stockroom.Domain.Users;

namespace Stockroom.Infrastructure.Persistence;

/// <summary>Demo-only credentials, supplied from configuration (appsettings.Development.json).</summary>
public sealed record DemoUser(string UserName, string Password, string Role);

/// <summary>
/// Seeds a small demo warehouse through the domain model (so every invariant and ledger rule applies):
/// dock + 2 storage zones (60 bins) + shipping, 15 products, opening stock, one open ASN, one new and one allocated order.
/// Idempotent: topology is only created into an empty database; missing users are added.
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, IPasswordHasher<User> hasher, TimeProvider clock, ILogger<DemoDataSeeder> logger)
{
    private static readonly (string Sku, string Barcode, string Name, decimal Volume, AbcClass Abc)[] Catalog =
    [
        ("MILK-1L", "4820000000011", "Milk 2.5% 1 l", 1.1m, AbcClass.A),
        ("YOG-500", "4820000000028", "Greek yogurt 500 g", 0.6m, AbcClass.A),
        ("CHS-200", "4820000000035", "Cheddar 200 g", 0.3m, AbcClass.B),
        ("JUICE-1L", "4820000000042", "Orange juice 1 l", 1.1m, AbcClass.A),
        ("WATER-6", "4820000000059", "Still water 6 x 1.5 l", 10m, AbcClass.A),
        ("COFFEE-1K", "4820000000066", "Coffee beans 1 kg", 2.5m, AbcClass.B),
        ("TEA-100", "4820000000073", "Black tea 100 bags", 0.8m, AbcClass.B),
        ("PASTA-500", "4820000000080", "Spaghetti 500 g", 0.9m, AbcClass.B),
        ("RICE-1K", "4820000000097", "Basmati rice 1 kg", 1.2m, AbcClass.C),
        ("OIL-1L", "4820000000103", "Sunflower oil 1 l", 1.1m, AbcClass.C),
        ("SOAP-4", "4820000000110", "Hand soap 4-pack", 0.7m, AbcClass.C),
        ("TP-12", "4820000000127", "Toilet paper 12 rolls", 18m, AbcClass.B),
        ("DET-3L", "4820000000134", "Laundry detergent 3 l", 3.3m, AbcClass.C),
        ("BAT-AA", "4820000000141", "Batteries AA 8-pack", 0.2m, AbcClass.C),
        ("CHOC-100", "4820000000158", "Dark chocolate 100 g", 0.15m, AbcClass.A),
    ];

    public async Task SeedAsync(IReadOnlyList<DemoUser> users, CancellationToken ct)
    {
        await SeedUsersAsync(users, ct);
        if (await db.Warehouses.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var warehouse = new Warehouse("MAIN", "Main distribution centre");
        var receiving = warehouse.AddZone("RCV", "Receiving dock", ZoneType.Receiving, AbcClass.A);
        receiving.AddLocation("RCV-01", 0, 0, 50_000);
        receiving.AddLocation("RCV-02", 0, 1, 50_000);
        var fast = warehouse.AddZone("A", "Fast movers", ZoneType.Storage, AbcClass.A);
        var slow = warehouse.AddZone("B", "Slow movers", ZoneType.Storage, AbcClass.B);
        AddAisles(fast, "A", firstAisle: 1);
        AddAisles(slow, "B", firstAisle: 4);
        warehouse.AddZone("SHP", "Shipping", ZoneType.Shipping, AbcClass.A).AddLocation("SHP-01", 0, 10, 50_000);
        db.Warehouses.Add(warehouse);

        var products = Catalog.ToDictionary(c => c.Sku, c => new Product(c.Sku, c.Barcode, c.Name, c.Volume, c.Abc));
        db.Products.AddRange(products.Values);

        var bins = fast.Locations.Concat(slow.Locations).ToDictionary(l => l.Code);
        var opening = new MovementContext("seed", "OPENING-BALANCE", now);
        var stock = new List<StockItem>();
        void Put(string sku, string lotNumber, DateOnly? expiry, int receivedDaysAgo, string bin, int quantity)
        {
            var lot = new Lot(products[sku], lotNumber, expiry, now.AddDays(-receivedDaysAgo));
            var item = new StockItem(bins[bin], lot);
            db.StockMovements.Add(StockLedger.Receive(item, quantity, opening));
            stock.Add(item);
        }

        // Two milk lots with different expiry: FEFO ships the earlier one first.
        Put("MILK-1L", "M-2401", today.AddDays(6), 10, "A-02-03-1", 40);
        Put("MILK-1L", "M-2402", today.AddDays(20), 2, "A-01-02-1", 60);
        Put("YOG-500", "Y-0107", today.AddDays(12), 5, "A-01-04-1", 50);
        Put("CHS-200", "C-0033", today.AddDays(45), 7, "B-04-01-1", 80);
        Put("JUICE-1L", "J-1101", today.AddDays(90), 14, "A-03-01-1", 48);
        Put("WATER-6", "W-0501", null, 30, "A-03-05-1", 20);
        Put("COFFEE-1K", "CF-77", today.AddDays(300), 60, "B-05-02-2", 30);
        Put("PASTA-500", "PS-12", null, 90, "B-05-04-1", 100);
        Put("PASTA-500", "PS-19", null, 20, "B-06-01-1", 100);
        Put("TP-12", "TP-03", null, 15, "B-04-05-2", 15);
        Put("CHOC-100", "CH-88", today.AddDays(3), 40, "A-01-05-2", 120);
        db.StockItems.AddRange(stock);

        var asn = new Asn("ASN-1001", "FreshFarm Dairy", now);
        asn.AddLine(products["MILK-1L"], 60);
        asn.AddLine(products["YOG-500"], 40);
        asn.AddLine(products["TEA-100"], 25);
        db.Asns.Add(asn);

        var newOrder = new Order("SO-2001", "Corner Market #12", now);
        newOrder.AddLine(products["PASTA-500"], 30);
        newOrder.AddLine(products["COFFEE-1K"], 4);
        db.Orders.Add(newOrder);

        // Pre-allocated so the terminal has pick tasks right away.
        var allocated = new Order("SO-2002", "City Grocery", now);
        allocated.AddLine(products["MILK-1L"], 50);
        allocated.AddLine(products["CHOC-100"], 24);
        allocated.AddLine(products["WATER-6"], 5);
        allocated.Allocate(stock, today);
        db.Orders.Add(allocated);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo warehouse with {Bins} storage bins and {Products} products", bins.Count, products.Count);
    }

    /// <summary>3 aisles x 5 bays x 2 levels per zone; code = zone-aisle-bay-level, X = aisle, Y = bay.</summary>
    private static void AddAisles(Zone zone, string prefix, int firstAisle)
    {
        for (var aisle = firstAisle; aisle < firstAisle + 3; aisle++)
        {
            for (var bay = 1; bay <= 5; bay++)
            {
                for (var level = 1; level <= 2; level++)
                {
                    zone.AddLocation($"{prefix}-{aisle:00}-{bay:00}-{level}", aisle, bay, level == 1 ? 400 : 250);
                }
            }
        }
    }

    private async Task SeedUsersAsync(IReadOnlyList<DemoUser> users, CancellationToken ct)
    {
        foreach (var demo in users)
        {
            var userName = demo.UserName.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
            {
                continue;
            }

            var user = new User(userName, demo.Role);
            user.SetPasswordHash(hasher.HashPassword(user, demo.Password));
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct);
    }
}
