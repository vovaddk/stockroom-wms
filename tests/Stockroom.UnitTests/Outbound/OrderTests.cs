using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;
using static Stockroom.UnitTests.TestWarehouse;

namespace Stockroom.UnitTests.Outbound;

public class OrderTests
{
    private readonly TestWarehouse _wh = new();
    private readonly Product _apple = Product("APPLE");
    private readonly Product _pear = Product("PEAR");

    public static TheoryData<OrderStatus, OrderStatus, bool> Transitions()
    {
        var allowed = new HashSet<(OrderStatus, OrderStatus)>
        {
            (OrderStatus.New, OrderStatus.Allocated),
            (OrderStatus.Allocated, OrderStatus.Picking),
            (OrderStatus.Picking, OrderStatus.Picked),
            (OrderStatus.Picked, OrderStatus.Shipped),
        };
        var data = new TheoryData<OrderStatus, OrderStatus, bool>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Only_forward_lifecycle_transitions_are_allowed(OrderStatus from, OrderStatus to, bool expected) =>
        Assert.Equal(expected, Order.CanTransition(from, to));

    [Fact]
    public void Shipping_a_new_order_throws()
    {
        var order = NewOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.Ship(Now));
        Assert.Equal(OrderStatus.New, order.Status);
    }

    [Fact]
    public void Allocation_reserves_stock_and_creates_tasks_in_walking_order()
    {
        var aisle1Far = Stock(_wh.ZoneA.AddLocation("A-01-05-1", 1, 5, 100), Lot(_apple, "A1", Today.AddDays(10)), 3);
        var aisle1Near = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_pear, "P1"), 4);
        var order = NewOrder((_apple, 3), (_pear, 4));

        order.Allocate([aisle1Far, aisle1Near], Today);

        Assert.Equal(OrderStatus.Allocated, order.Status);
        Assert.Equal(3, aisle1Far.ReservedQuantity);
        Assert.Equal(4, aisle1Near.ReservedQuantity);
        Assert.Equal(["A-01-01-1", "A-01-05-1"], order.PickTasks.OrderBy(t => t.Sequence).Select(t => t.StockItem.Location.Code));
    }

    [Fact]
    public void Allocation_is_all_or_nothing()
    {
        var apples = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), 10);
        var pears = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_pear), 1);
        var order = NewOrder((_apple, 5), (_pear, 2));

        var ex = Assert.Throws<DomainException>(() => order.Allocate([apples, pears], Today));

        Assert.Contains("PEAR", ex.Message);
        Assert.Equal(0, apples.ReservedQuantity);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Empty(order.PickTasks);
    }

    [Fact]
    public void Allocating_twice_throws()
    {
        var stock = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), 10);
        var order = NewOrder((_apple, 2));
        order.Allocate([stock], Today);

        Assert.Throws<InvalidStateTransitionException>(() => order.Allocate([stock], Today));
        Assert.Equal(2, stock.ReservedQuantity);
    }

    [Fact]
    public void Full_pick_moves_order_through_picking_to_picked_and_ships()
    {
        var (order, stock) = AllocatedOrder(onHand: 10, ordered: 4);
        var task = order.PickTasks.Single();

        var movement = order.ConfirmPick(task, "a-01-01-1", _apple.Barcode, 4, Context);

        Assert.Equal(StockMovementType.Pick, movement!.Type);
        Assert.Equal(PickTaskStatus.Picked, task.Status);
        Assert.Equal(OrderStatus.Picked, order.Status);
        Assert.Equal(6, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(4, order.Lines.Single().PickedQuantity);

        order.Ship(Now);
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Order_is_picking_while_tasks_remain_open()
    {
        var bin1 = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), 5);
        var bin2 = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_pear), 5);
        var order = NewOrder((_apple, 1), (_pear, 1));
        order.Allocate([bin1, bin2], Today);
        var first = order.PickTasks.OrderBy(t => t.Sequence).First();

        order.ConfirmPick(first, "A-01-01-1", _apple.Barcode, 1, Context);

        Assert.Equal(OrderStatus.Picking, order.Status);
        Assert.Throws<InvalidStateTransitionException>(() => order.Ship(Now));
    }

    [Fact]
    public void Short_pick_releases_whole_reservation_and_completes_task_as_short()
    {
        var (order, stock) = AllocatedOrder(onHand: 10, ordered: 5);
        var task = order.PickTasks.Single();

        order.ConfirmPick(task, "A-01-01-1", _apple.Barcode, 2, Context);

        Assert.Equal(PickTaskStatus.Short, task.Status);
        Assert.Equal(2, task.PickedQuantity);
        Assert.Equal(8, stock.Quantity);
        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(OrderStatus.Picked, order.Status);
    }

    [Fact]
    public void Zero_pick_records_no_movement()
    {
        var (order, stock) = AllocatedOrder(onHand: 10, ordered: 5);

        var movement = order.ConfirmPick(order.PickTasks.Single(), "A-01-01-1", _apple.Barcode, 0, Context);

        Assert.Null(movement);
        Assert.Equal(10, stock.Quantity);
        Assert.Equal(PickTaskStatus.Short, order.PickTasks.Single().Status);
    }

    [Theory]
    [InlineData("A-09-09-9", "BC-APPLE", 1)]
    [InlineData("A-01-01-1", "BC-WRONG", 1)]
    [InlineData("A-01-01-1", "BC-APPLE", 6)]
    [InlineData("A-01-01-1", "BC-APPLE", -1)]
    public void Invalid_scans_or_quantities_are_rejected_without_side_effects(string location, string barcode, int quantity)
    {
        var (order, stock) = AllocatedOrder(onHand: 10, ordered: 5);
        var task = order.PickTasks.Single();

        Assert.Throws<DomainException>(() => order.ConfirmPick(task, location, barcode, quantity, Context));

        Assert.Equal(PickTaskStatus.Open, task.Status);
        Assert.Equal(10, stock.Quantity);
        Assert.Equal(5, stock.ReservedQuantity);
        Assert.Equal(OrderStatus.Allocated, order.Status);
    }

    [Fact]
    public void Confirming_the_same_task_twice_is_a_state_conflict()
    {
        var bin1 = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), 5);
        var bin2 = Stock(_wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100), Lot(_pear), 5);
        var order = NewOrder((_apple, 1), (_pear, 1));
        order.Allocate([bin1, bin2], Today);
        var first = order.PickTasks.OrderBy(t => t.Sequence).First();
        order.ConfirmPick(first, "A-01-01-1", _apple.Barcode, 1, Context);

        Assert.Throws<InvalidStateTransitionException>(() =>
            order.ConfirmPick(first, "A-01-01-1", _apple.Barcode, 1, Context));
        Assert.Equal(4, bin1.Quantity);
    }

    [Fact]
    public void Lines_cannot_be_added_after_allocation_or_duplicated()
    {
        var order = NewOrder((_apple, 1));

        Assert.Throws<DomainException>(() => order.AddLine(_apple, 2));

        order.Allocate([Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), 5)], Today);
        Assert.Throws<DomainException>(() => order.AddLine(_pear, 1));
    }

    private (Order Order, StockItem Stock) AllocatedOrder(int onHand, int ordered)
    {
        var stock = Stock(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), Lot(_apple), onHand);
        var order = NewOrder((_apple, ordered));
        order.Allocate([stock], Today);
        return (order, stock);
    }

    private static Order NewOrder(params (Product Product, int Quantity)[] lines)
    {
        var order = new Order("SO-1", "Customer", Now);
        foreach (var (product, quantity) in lines)
        {
            order.AddLine(product, quantity);
        }

        return order;
    }
}
