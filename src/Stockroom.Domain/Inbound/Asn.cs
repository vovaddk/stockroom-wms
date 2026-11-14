using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;

namespace Stockroom.Domain.Inbound;

public enum AsnStatus
{
    Open,
    Closed,
}

/// <summary>Advance shipping notice: what a supplier says is on the truck. Receipts are checked against it.</summary>
public sealed class Asn
{
    private readonly List<AsnLine> _lines = [];

    private Asn() { }

    public Asn(string number, string supplier, DateTimeOffset createdAt)
    {
        Number = Guard.Code(number, nameof(Number));
        Supplier = Guard.NotBlank(supplier, nameof(Supplier));
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string Number { get; private set; } = null!;
    public string Supplier { get; private set; } = null!;
    public AsnStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<AsnLine> Lines => _lines;

    public void AddLine(Product product, int expectedQuantity)
    {
        EnsureOpen();
        if (_lines.Any(l => l.Product.Sku == product.Sku))
        {
            throw new DomainException($"ASN {Number} already has a line for {product.Sku}.");
        }

        _lines.Add(new AsnLine(product, expectedQuantity));
    }

    /// <summary>Registers a (partial) receipt. Over-receipt and unexpected products are rejected; the ASN closes when every line is complete.</summary>
    public AsnLine Receive(Product product, int quantity)
    {
        EnsureOpen();
        var line = _lines.FirstOrDefault(l => l.Product.Sku == product.Sku)
            ?? throw new DomainException($"{product.Sku} is not on ASN {Number}.");

        line.Receive(quantity);
        if (_lines.All(l => l.IsComplete))
        {
            Status = AsnStatus.Closed;
        }

        return line;
    }

    private void EnsureOpen()
    {
        if (Status != AsnStatus.Open)
        {
            throw new DomainException($"ASN {Number} is {Status}.");
        }
    }
}

public sealed class AsnLine
{
    private AsnLine() { }

    internal AsnLine(Product product, int expectedQuantity)
    {
        Product = product;
        ExpectedQuantity = Guard.Positive(expectedQuantity, nameof(ExpectedQuantity));
    }

    public int Id { get; private set; }
    public int AsnId { get; private set; }
    public int ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int ExpectedQuantity { get; private set; }
    public int ReceivedQuantity { get; private set; }

    public int RemainingQuantity => ExpectedQuantity - ReceivedQuantity;
    public bool IsComplete => RemainingQuantity == 0;

    internal void Receive(int quantity)
    {
        Guard.Positive(quantity, nameof(quantity));
        if (quantity > RemainingQuantity)
        {
            throw new DomainException(
                $"Over-receipt of {Product.Sku}: expected {ExpectedQuantity}, already received {ReceivedQuantity}, scanned {quantity}.");
        }

        ReceivedQuantity += quantity;
    }
}
