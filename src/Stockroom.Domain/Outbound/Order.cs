using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;

namespace Stockroom.Domain.Outbound;

public enum OrderStatus
{
    New,
    Allocated,
    Picking,
    Picked,
    Shipped,
}

/// <summary>Customer order. Status follows New → Allocated → Picking → Picked → Shipped; anything else throws.</summary>
public sealed class Order
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> AllowedTransitions =
    [
        (OrderStatus.New, OrderStatus.Allocated),
        (OrderStatus.Allocated, OrderStatus.Picking),
        (OrderStatus.Picking, OrderStatus.Picked),
        (OrderStatus.Picked, OrderStatus.Shipped),
    ];

    private readonly List<OrderLine> _lines = [];
    private readonly List<PickTask> _pickTasks = [];

    private Order() { }

    public Order(string number, string customer, DateTimeOffset createdAt)
    {
        Number = Guard.Code(number, nameof(Number));
        Customer = Guard.NotBlank(customer, nameof(Customer));
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string Number { get; private set; } = null!;
    public string Customer { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public IReadOnlyCollection<PickTask> PickTasks => _pickTasks;

    public static bool CanTransition(OrderStatus from, OrderStatus to) => AllowedTransitions.Contains((from, to));

    public void AddLine(Product product, int quantity)
    {
        if (Status != OrderStatus.New)
        {
            throw new DomainException($"Order {Number} is {Status}; lines can only be added to new orders.");
        }

        if (_lines.Any(l => l.Product.Sku == product.Sku))
        {
            throw new DomainException($"Order {Number} already has a line for {product.Sku}.");
        }

        _lines.Add(new OrderLine(product, quantity));
    }

    /// <summary>
    /// Reserves stock for every line (FEFO) and creates pick tasks in walking order.
    /// All-or-nothing: if any line cannot be covered nothing is reserved.
    /// </summary>
    public void Allocate(IReadOnlyCollection<StockItem> stock, DateOnly today)
    {
        EnsureCanTransition(OrderStatus.Allocated);
        if (_lines.Count == 0)
        {
            throw new DomainException($"Order {Number} has no lines.");
        }

        var planned = new List<(OrderLine Line, Allocation Allocation)>();
        foreach (var line in _lines)
        {
            try
            {
                var candidates = stock.Where(s => s.Product.Sku == line.Product.Sku);
                planned.AddRange(FefoAllocator.Allocate(line.Quantity, candidates, today).Select(a => (line, a)));
            }
            catch (DomainException ex)
            {
                throw new DomainException($"Cannot allocate {line.Product.Sku} for order {Number}. {ex.Message}");
            }
        }

        var sequence = 1;
        foreach (var (line, allocation) in PickPathSequencer.Sequence(planned, p => p.Allocation.Stock.Location))
        {
            allocation.Stock.Reserve(allocation.Quantity);
            _pickTasks.Add(new PickTask(this, line, allocation.Stock, allocation.Quantity, sequence++));
        }

        Status = OrderStatus.Allocated;
    }

    /// <summary>
    /// Confirms a scanned pick. A quantity below the task quantity is a short pick: the rest of the reservation
    /// is released and the order ships short. Returns the ledger entry, or <c>null</c> when nothing was picked.
    /// </summary>
    public StockMovement? ConfirmPick(
        PickTask task, string scannedLocation, string scannedBarcode, int quantity, MovementContext context)
    {
        if (Status is not (OrderStatus.Allocated or OrderStatus.Picking))
        {
            throw new InvalidStateTransitionException(nameof(Order), Status, OrderStatus.Picking);
        }

        if (!_pickTasks.Contains(task))
        {
            throw new DomainException($"Pick task {task.Id} does not belong to order {Number}.");
        }

        task.Confirm(scannedLocation, scannedBarcode, quantity, context.UserName, context.OccurredAt);
        task.StockItem.Release(task.Quantity);
        var movement = quantity > 0 ? StockLedger.Pick(task.StockItem, quantity, context) : null;
        task.OrderLine.RegisterPicked(quantity);

        if (Status == OrderStatus.Allocated)
        {
            TransitionTo(OrderStatus.Picking);
        }

        if (_pickTasks.All(t => t.Status != PickTaskStatus.Open))
        {
            TransitionTo(OrderStatus.Picked);
        }

        return movement;
    }

    public void Ship(DateTimeOffset at)
    {
        TransitionTo(OrderStatus.Shipped);
        ShippedAt = at;
    }

    private void TransitionTo(OrderStatus target)
    {
        EnsureCanTransition(target);
        Status = target;
    }

    private void EnsureCanTransition(OrderStatus target)
    {
        if (!CanTransition(Status, target))
        {
            throw new InvalidStateTransitionException(nameof(Order), Status, target);
        }
    }
}

public sealed class OrderLine
{
    private OrderLine() { }

    internal OrderLine(Product product, int quantity)
    {
        Product = product;
        Quantity = Guard.Positive(quantity, nameof(quantity));
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int PickedQuantity { get; private set; }

    internal void RegisterPicked(int quantity) => PickedQuantity += quantity;
}
