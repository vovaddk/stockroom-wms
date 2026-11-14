using Stockroom.Domain.Common;

namespace Stockroom.Domain.Catalog;

/// <summary>A batch of one product. Expiry is optional: non-perishables are allocated FIFO by <see cref="ReceivedAt"/>.</summary>
public sealed class Lot
{
    private Lot() { }

    public Lot(Product product, string lotNumber, DateOnly? expiryDate, DateTimeOffset receivedAt)
    {
        Product = product;
        LotNumber = Guard.Code(lotNumber, nameof(LotNumber));
        ExpiryDate = expiryDate;
        ReceivedAt = receivedAt;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public string LotNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    public bool IsExpiredOn(DateOnly date) => ExpiryDate < date;

    public void EnsureExpiryMatches(DateOnly? expiryDate)
    {
        if (expiryDate is not null && expiryDate != ExpiryDate)
        {
            var existing = ExpiryDate?.ToString("yyyy-MM-dd") ?? "none";
            throw new DomainException($"Lot {LotNumber} already exists with expiry {existing}; received {expiryDate:yyyy-MM-dd}.");
        }
    }
}
