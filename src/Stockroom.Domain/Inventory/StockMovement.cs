using Stockroom.Domain.Catalog;
using Stockroom.Domain.Topology;

namespace Stockroom.Domain.Inventory;

public enum StockMovementType
{
    Receipt,
    PutAway,
    Pick,
    Transfer,
    Adjustment,
}

/// <summary>Immutable ledger entry. Created only by <see cref="StockLedger"/>; never updated or deleted.</summary>
public sealed class StockMovement
{
    private StockMovement() { }

    internal StockMovement(
        StockMovementType type, Lot lot, Location? from, Location? to, int quantity, MovementContext context, string? reason = null)
    {
        Type = type;
        Product = lot.Product;
        Lot = lot;
        FromLocation = from;
        ToLocation = to;
        Quantity = quantity;
        UserName = context.UserName;
        Reference = context.Reference;
        OccurredAt = context.OccurredAt;
        Reason = reason;
    }

    public long Id { get; private set; }
    public StockMovementType Type { get; private set; }
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LotId { get; private set; }
    public Lot Lot { get; private set; } = null!;
    public int? FromLocationId { get; private set; }
    public Location? FromLocation { get; private set; }
    public int? ToLocationId { get; private set; }
    public Location? ToLocation { get; private set; }
    public int Quantity { get; private set; }
    public string UserName { get; private set; } = null!;

    /// <summary>Source document: ASN number, order number, task id or count reference.</summary>
    public string Reference { get; private set; } = null!;

    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

public sealed record MovementContext(string UserName, string Reference, DateTimeOffset OccurredAt);
