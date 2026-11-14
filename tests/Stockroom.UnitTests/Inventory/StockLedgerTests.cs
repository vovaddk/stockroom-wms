using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;
using static Stockroom.UnitTests.TestWarehouse;

namespace Stockroom.UnitTests.Inventory;

public class StockLedgerTests
{
    private readonly TestWarehouse _wh = new();

    [Fact]
    public void Receive_increases_quantity_and_records_receipt_movement()
    {
        var lot = Lot(Product());
        var stock = new StockItem(_wh.Dock, lot);

        var movement = StockLedger.Receive(stock, 10, Context);

        Assert.Equal(10, stock.Quantity);
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Null(movement.FromLocation);
        Assert.Same(_wh.Dock, movement.ToLocation);
        Assert.Equal(10, movement.Quantity);
        Assert.Equal("TEST-REF", movement.Reference);
    }

    [Fact]
    public void Pick_more_than_on_hand_throws_and_leaves_stock_unchanged()
    {
        var stock = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(Product()), 5);

        Assert.Throws<DomainException>(() => StockLedger.Pick(stock, 6, Context));
        Assert.Equal(5, stock.Quantity);
    }

    [Fact]
    public void Reserved_stock_cannot_be_moved_by_someone_else()
    {
        var bin = _wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100);
        var lot = Lot(Product());
        var stock = Stock(bin, lot, 5);
        stock.Reserve(4);
        var target = new StockItem(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), lot);

        Assert.Throws<DomainException>(() => StockLedger.Move(StockMovementType.Transfer, stock, target, 2, Context));
        Assert.Equal(5, stock.Quantity);
        Assert.Equal(0, target.Quantity);
    }

    [Fact]
    public void Move_transfers_quantity_between_bins()
    {
        var lot = Lot(Product());
        var source = Stock(_wh.Dock, lot, 8);
        var target = new StockItem(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), lot);

        var movement = StockLedger.Move(StockMovementType.PutAway, source, target, 8, Context);

        Assert.Equal(0, source.Quantity);
        Assert.Equal(8, target.Quantity);
        Assert.Same(_wh.Dock, movement.FromLocation);
        Assert.Same(target.Location, movement.ToLocation);
    }

    [Fact]
    public void Move_into_blocked_bin_is_rejected()
    {
        var lot = Lot(Product());
        var source = Stock(_wh.Dock, lot, 8);
        var blocked = _wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100);
        blocked.Block();

        Assert.Throws<DomainException>(() =>
            StockLedger.Move(StockMovementType.Transfer, source, new StockItem(blocked, lot), 1, Context));
        Assert.Equal(8, source.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_quantities_are_rejected(int quantity)
    {
        var stock = new StockItem(_wh.Dock, Lot(Product()));

        Assert.Throws<DomainException>(() => StockLedger.Receive(stock, quantity, Context));
    }

    [Fact]
    public void Adjust_down_records_outbound_adjustment_with_reason()
    {
        var stock = Stock(_wh.Dock, Lot(Product()), 10);

        var movement = StockLedger.Adjust(stock, 7, "Damaged", Context);

        Assert.NotNull(movement);
        Assert.Equal(7, stock.Quantity);
        Assert.Equal(3, movement.Quantity);
        Assert.Same(_wh.Dock, movement.FromLocation);
        Assert.Equal("Damaged", movement.Reason);
    }

    [Fact]
    public void Adjust_up_records_inbound_adjustment()
    {
        var stock = Stock(_wh.Dock, Lot(Product()), 10);

        var movement = StockLedger.Adjust(stock, 12, "Found in count", Context);

        Assert.Equal(12, stock.Quantity);
        Assert.Same(_wh.Dock, movement!.ToLocation);
        Assert.Equal(2, movement.Quantity);
    }

    [Fact]
    public void Adjust_to_same_quantity_records_nothing()
    {
        var stock = Stock(_wh.Dock, Lot(Product()), 10);

        Assert.Null(StockLedger.Adjust(stock, 10, "Cycle count", Context));
    }

    [Fact]
    public void Adjust_below_reserved_quantity_is_rejected()
    {
        var stock = Stock(_wh.Dock, Lot(Product()), 10);
        stock.Reserve(6);

        Assert.Throws<DomainException>(() => StockLedger.Adjust(stock, 5, "Count", Context));
        Assert.Equal(10, stock.Quantity);
    }

    [Fact]
    public void Adjust_to_negative_is_rejected()
    {
        var stock = Stock(_wh.Dock, Lot(Product()), 1);

        Assert.Throws<DomainException>(() => StockLedger.Adjust(stock, -1, "Count", Context));
    }
}
