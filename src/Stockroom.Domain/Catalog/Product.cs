using Stockroom.Domain.Common;
using Stockroom.Domain.Topology;

namespace Stockroom.Domain.Catalog;

public sealed class Product
{
    private Product() { }

    public Product(string sku, string barcode, string name, decimal unitVolume, AbcClass abcClass)
    {
        Sku = Guard.Code(sku, nameof(Sku));
        Barcode = Guard.NotBlank(barcode, nameof(Barcode));
        Name = Guard.NotBlank(name, nameof(Name));
        UnitVolume = Guard.Positive(unitVolume, nameof(UnitVolume));
        AbcClass = abcClass;
    }

    public int Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Barcode { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    /// <summary>Volume of one unit in litres.</summary>
    public decimal UnitVolume { get; private set; }

    public AbcClass AbcClass { get; private set; }

    public decimal VolumeOf(int quantity) => UnitVolume * quantity;
}
