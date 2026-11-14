using Stockroom.Domain.Common;
using Stockroom.Domain.Outbound;
using static Stockroom.UnitTests.TestWarehouse;

namespace Stockroom.UnitTests.Outbound;

public class FefoAllocatorTests
{
    private readonly TestWarehouse _wh = new();
    private readonly Domain.Catalog.Product _product = Product();

    [Fact]
    public void Earliest_expiry_is_allocated_first()
    {
        var late = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product, "LATE", Today.AddDays(60)), 10);
        var early = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_product, "EARLY", Today.AddDays(5)), 10);

        var result = FefoAllocator.Allocate(4, [late, early], Today);

        var allocation = Assert.Single(result);
        Assert.Same(early, allocation.Stock);
        Assert.Equal(4, allocation.Quantity);
    }

    [Fact]
    public void Allocation_spills_over_to_next_lot_when_first_is_not_enough()
    {
        var early = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product, "EARLY", Today.AddDays(5)), 3);
        var late = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_product, "LATE", Today.AddDays(60)), 10);

        var result = FefoAllocator.Allocate(5, [late, early], Today);

        Assert.Collection(result,
            a => { Assert.Same(early, a.Stock); Assert.Equal(3, a.Quantity); },
            a => { Assert.Same(late, a.Stock); Assert.Equal(2, a.Quantity); });
    }

    [Fact]
    public void Non_expiring_lots_come_after_expiring_ones_and_are_fifo_by_receipt()
    {
        var newer = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product, "NEW", receivedDaysAgo: 1), 5);
        var older = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_product, "OLD", receivedDaysAgo: 30), 5);
        var expiring = Stock(_wh.ZoneA.AddLocation("A-01-03-1", 1, 3, 100), Lot(_product, "EXP", Today.AddDays(300)), 5);

        var result = FefoAllocator.Allocate(15, [newer, older, expiring], Today);

        Assert.Equal([expiring, older, newer], result.Select(a => a.Stock));
    }

    [Fact]
    public void Expired_blocked_reserved_and_unputaway_stock_is_skipped()
    {
        var expired = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product, "OLD", Today.AddDays(-1)), 10);
        var blockedBin = _wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100);
        var blocked = Stock(blockedBin, Lot(_product, "B1", Today.AddDays(1)), 10);
        blockedBin.Block();
        var onDock = Stock(_wh.Dock, Lot(_product, "DOCK", Today.AddDays(2)), 10);
        var reserved = Stock(_wh.ZoneA.AddLocation("A-01-03-1", 1, 3, 100), Lot(_product, "RES", Today.AddDays(3)), 10);
        reserved.Reserve(10);
        var good = Stock(_wh.ZoneA.AddLocation("A-01-04-1", 1, 4, 100), Lot(_product, "GOOD", Today.AddDays(90)), 10);

        var result = FefoAllocator.Allocate(5, [expired, blocked, onDock, reserved, good], Today);

        Assert.Same(good, Assert.Single(result).Stock);
    }

    [Fact]
    public void Lot_expiring_today_is_still_allocatable()
    {
        var today = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product, "TODAY", Today), 1);

        Assert.Single(FefoAllocator.Allocate(1, [today], Today));
    }

    [Fact]
    public void Insufficient_stock_throws()
    {
        var stock = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_product), 3);

        var ex = Assert.Throws<DomainException>(() => FefoAllocator.Allocate(4, [stock], Today));
        Assert.Contains("requested 4, available 3", ex.Message);
    }
}
