using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;

namespace Stockroom.Domain.Outbound;

public sealed record Allocation(StockItem Stock, int Quantity);

/// <summary>
/// First-expired-first-out: lots with the earliest expiry ship first; lots without expiry follow,
/// oldest receipt first (FIFO). Expired lots, blocked bins and stock not yet put away are skipped.
/// </summary>
public static class FefoAllocator
{
    public static IReadOnlyList<Allocation> Allocate(int quantity, IEnumerable<StockItem> candidates, DateOnly today)
    {
        Guard.Positive(quantity, nameof(quantity));

        var ordered = candidates
            .Where(s => s.AvailableQuantity > 0
                && s.Location.Zone.IsStorage
                && !s.Location.IsBlocked
                && !s.Lot.IsExpiredOn(today))
            .OrderBy(s => s.Lot.ExpiryDate is null)
            .ThenBy(s => s.Lot.ExpiryDate)
            .ThenBy(s => s.Lot.ReceivedAt)
            .ThenBy(s => s.Location.Code, StringComparer.Ordinal);

        var allocations = new List<Allocation>();
        var remaining = quantity;
        foreach (var stock in ordered)
        {
            var take = Math.Min(remaining, stock.AvailableQuantity);
            allocations.Add(new Allocation(stock, take));
            remaining -= take;
            if (remaining == 0)
            {
                return allocations;
            }
        }

        throw new DomainException($"Insufficient stock: requested {quantity}, available {quantity - remaining}.");
    }
}
