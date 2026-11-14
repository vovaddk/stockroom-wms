using Stockroom.Domain.Common;

namespace Stockroom.Domain.Inventory;

/// <summary>
/// The only way to change physical stock: every call updates the <see cref="StockItem"/> projection
/// and returns the matching ledger entry. The caller persists both in one transaction.
/// </summary>
public static class StockLedger
{
    public static StockMovement Receive(StockItem target, int quantity, MovementContext context)
    {
        target.Location.EnsureUsable();
        target.Add(quantity);
        return new StockMovement(StockMovementType.Receipt, target.Lot, null, target.Location, quantity, context);
    }

    public static StockMovement Move(
        StockMovementType type, StockItem source, StockItem target, int quantity, MovementContext context)
    {
        if (type is not (StockMovementType.PutAway or StockMovementType.Transfer))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Only put-away and transfer move stock between locations.");
        }

        if (!ReferenceEquals(source.Lot, target.Lot))
        {
            throw new DomainException("Source and target must hold the same lot.");
        }

        if (ReferenceEquals(source.Location, target.Location))
        {
            throw new DomainException($"Source and target location are the same ({source.Location.Code}).");
        }

        target.Location.EnsureUsable();
        source.Remove(quantity);
        target.Add(quantity);
        return new StockMovement(type, source.Lot, source.Location, target.Location, quantity, context);
    }

    public static StockMovement Pick(StockItem source, int quantity, MovementContext context)
    {
        source.Remove(quantity);
        return new StockMovement(StockMovementType.Pick, source.Lot, source.Location, null, quantity, context);
    }

    /// <summary>Records a physical count. Returns <c>null</c> when the count matches the system quantity.</summary>
    public static StockMovement? Adjust(StockItem item, int countedQuantity, string reason, MovementContext context)
    {
        reason = Guard.NotBlank(reason, nameof(reason));
        if (countedQuantity < 0)
        {
            throw new DomainException("Counted quantity cannot be negative.");
        }

        if (countedQuantity < item.ReservedQuantity)
        {
            throw new DomainException(
                $"Counted {countedQuantity} is below the {item.ReservedQuantity} reserved for open picks at {item.Location.Code}. Resolve the picks first.");
        }

        var delta = countedQuantity - item.Quantity;
        if (delta == 0)
        {
            return null;
        }

        if (delta > 0)
        {
            item.Add(delta);
            return new StockMovement(StockMovementType.Adjustment, item.Lot, null, item.Location, delta, context, reason);
        }

        item.Remove(-delta);
        return new StockMovement(StockMovementType.Adjustment, item.Lot, item.Location, null, -delta, context, reason);
    }
}
