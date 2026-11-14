using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;

namespace Stockroom.Domain.Outbound;

public enum PickTaskStatus
{
    Open,
    Picked,
    Short,
}

/// <summary>Pick a quantity of one reserved stock item. Confirmed only through <see cref="Order.ConfirmPick"/>.</summary>
public sealed class PickTask
{
    private PickTask() { }

    internal PickTask(Order order, OrderLine line, StockItem stock, int quantity, int sequence)
    {
        Order = order;
        OrderLine = line;
        StockItem = stock;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        Sequence = sequence;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public Order Order { get; private set; } = null!;
    public int OrderLineId { get; private set; }
    public OrderLine OrderLine { get; private set; } = null!;
    public int StockItemId { get; private set; }
    public StockItem StockItem { get; private set; } = null!;

    /// <summary>Position in the walking route (1 = first stop).</summary>
    public int Sequence { get; private set; }

    public int Quantity { get; private set; }
    public int PickedQuantity { get; private set; }
    public PickTaskStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CompletedBy { get; private set; }

    internal void Confirm(string scannedLocation, string scannedBarcode, int quantity, string userName, DateTimeOffset at)
    {
        if (Status != PickTaskStatus.Open)
        {
            throw new InvalidStateTransitionException("Pick task", Status, PickTaskStatus.Picked);
        }

        var expectedLocation = StockItem.Location.Code;
        if (!string.Equals(scannedLocation.Trim(), expectedLocation, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Wrong location: go to {expectedLocation}, scanned {scannedLocation}.");
        }

        if (scannedBarcode.Trim() != StockItem.Product.Barcode)
        {
            throw new DomainException($"Wrong product: expected {StockItem.Product.Sku} ({StockItem.Product.Barcode}), scanned {scannedBarcode}.");
        }

        if (quantity < 0 || quantity > Quantity)
        {
            throw new DomainException($"Picked quantity must be between 0 and {Quantity}.");
        }

        PickedQuantity = quantity;
        Status = quantity == Quantity ? PickTaskStatus.Picked : PickTaskStatus.Short;
        CompletedAt = at;
        CompletedBy = userName;
    }
}
