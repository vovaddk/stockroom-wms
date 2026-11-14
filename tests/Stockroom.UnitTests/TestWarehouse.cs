using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Topology;

namespace Stockroom.UnitTests;

/// <summary>Small in-memory warehouse: a dock at (0,0) and two storage zones (A and B class).</summary>
internal sealed class TestWarehouse
{
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    public static readonly MovementContext Context = new("tester", "TEST-REF", Now);

    public TestWarehouse()
    {
        Warehouse = new Warehouse("WH1", "Test warehouse");
        Receiving = Warehouse.AddZone("RCV", "Receiving", ZoneType.Receiving, AbcClass.A);
        ZoneA = Warehouse.AddZone("A", "Fast movers", ZoneType.Storage, AbcClass.A);
        ZoneB = Warehouse.AddZone("B", "Slow movers", ZoneType.Storage, AbcClass.B);
        Dock = Receiving.AddLocation("RCV-01", 0, 0, 10_000);
    }

    public Warehouse Warehouse { get; }
    public Zone Receiving { get; }
    public Zone ZoneA { get; }
    public Zone ZoneB { get; }
    public Location Dock { get; }

    public static Product Product(string sku = "SKU-1", decimal unitVolume = 1m, AbcClass abc = AbcClass.A) =>
        new(sku, $"BC-{sku}", $"Product {sku}", unitVolume, abc);

    public static Lot Lot(Product product, string number = "LOT-1", DateOnly? expiry = null, int receivedDaysAgo = 0) =>
        new(product, number, expiry, Now.AddDays(-receivedDaysAgo));

    public static StockItem Stock(Location location, Lot lot, int quantity)
    {
        var stock = new StockItem(location, lot);
        StockLedger.Receive(stock, quantity, Context);
        return stock;
    }
}
