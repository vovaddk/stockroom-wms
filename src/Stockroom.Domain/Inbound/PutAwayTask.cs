using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;
using Stockroom.Domain.Topology;

namespace Stockroom.Domain.Inbound;

public enum PutAwayTaskStatus
{
    Open,
    Completed,
}

/// <summary>Instruction to move received goods from the dock to a storage bin.</summary>
public sealed class PutAwayTask
{
    private PutAwayTask() { }

    public PutAwayTask(Lot lot, Location fromLocation, Location? suggestedLocation, int quantity, DateTimeOffset createdAt)
    {
        Lot = lot;
        Product = lot.Product;
        FromLocation = fromLocation;
        SuggestedLocation = suggestedLocation;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int LotId { get; private set; }
    public Lot Lot { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int FromLocationId { get; private set; }
    public Location FromLocation { get; private set; } = null!;
    public int? SuggestedLocationId { get; private set; }
    public Location? SuggestedLocation { get; private set; }
    public int? ActualLocationId { get; private set; }
    public Location? ActualLocation { get; private set; }
    public PutAwayTaskStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CompletedBy { get; private set; }

    /// <summary>The operator must scan the suggested bin; without a suggestion any usable storage bin is accepted.</summary>
    public void Complete(Location scanned, string userName, DateTimeOffset at)
    {
        if (Status != PutAwayTaskStatus.Open)
        {
            throw new InvalidStateTransitionException("Put-away task", Status, PutAwayTaskStatus.Completed);
        }

        if (SuggestedLocation is not null && scanned.Code != SuggestedLocation.Code)
        {
            throw new DomainException($"Wrong location: put away to {SuggestedLocation.Code}, scanned {scanned.Code}.");
        }

        if (!scanned.Zone.IsStorage)
        {
            throw new DomainException($"{scanned.Code} is not a storage location.");
        }

        scanned.EnsureUsable();
        ActualLocation = scanned;
        Status = PutAwayTaskStatus.Completed;
        CompletedAt = at;
        CompletedBy = userName;
    }
}
