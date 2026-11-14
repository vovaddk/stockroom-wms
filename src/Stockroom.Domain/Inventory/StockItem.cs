using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;
using Stockroom.Domain.Topology;

namespace Stockroom.Domain.Inventory;

/// <summary>
/// Projection of the movement ledger: quantity of one lot in one location.
/// Physical quantity changes only via <see cref="StockLedger"/> (the mutators are internal);
/// reservations are not physical moves, so they are public.
/// </summary>
public sealed class StockItem
{
    private StockItem() { }

    public StockItem(Location location, Lot lot)
    {
        Location = location;
        Lot = lot;
        Product = lot.Product;
    }

    public int Id { get; private set; }
    public int LocationId { get; private set; }
    public Location Location { get; private set; } = null!;
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LotId { get; private set; }
    public Lot Lot { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    public int AvailableQuantity => Quantity - ReservedQuantity;

    public void Reserve(int quantity)
    {
        Guard.Positive(quantity, nameof(quantity));
        if (quantity > AvailableQuantity)
        {
            throw new DomainException($"Cannot reserve {quantity} of {Product.Sku} at {Location.Code}: only {AvailableQuantity} available.");
        }

        ReservedQuantity += quantity;
    }

    public void Release(int quantity)
    {
        Guard.Positive(quantity, nameof(quantity));
        if (quantity > ReservedQuantity)
        {
            throw new DomainException($"Cannot release {quantity} of {Product.Sku} at {Location.Code}: only {ReservedQuantity} reserved.");
        }

        ReservedQuantity -= quantity;
    }

    internal void Add(int quantity) => Quantity += Guard.Positive(quantity, nameof(quantity));

    internal void Remove(int quantity)
    {
        Guard.Positive(quantity, nameof(quantity));
        if (quantity > AvailableQuantity)
        {
            throw new DomainException(
                $"Insufficient stock of {Product.Sku} lot {Lot.LotNumber} at {Location.Code}: {AvailableQuantity} available, {quantity} requested.");
        }

        Quantity -= quantity;
    }
}
