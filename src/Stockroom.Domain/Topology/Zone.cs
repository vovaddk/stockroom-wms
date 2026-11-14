using Stockroom.Domain.Common;

namespace Stockroom.Domain.Topology;

public sealed class Zone
{
    private readonly List<Location> _locations = [];

    private Zone() { }

    internal Zone(Warehouse warehouse, string code, string name, ZoneType type, AbcClass abcClass)
    {
        Warehouse = warehouse;
        Code = Guard.Code(code, nameof(Code));
        Name = Guard.NotBlank(name, nameof(Name));
        Type = type;
        AbcClass = abcClass;
    }

    public int Id { get; private set; }
    public int WarehouseId { get; private set; }
    public Warehouse Warehouse { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ZoneType Type { get; private set; }
    public AbcClass AbcClass { get; private set; }
    public IReadOnlyCollection<Location> Locations => _locations;

    /// <summary>Stock in these zones has been put away and can be allocated to orders.</summary>
    public bool IsStorage => Type is ZoneType.Storage or ZoneType.Picking;

    public Location AddLocation(string code, int x, int y, decimal maxVolume)
    {
        var location = new Location(this, code, x, y, maxVolume);
        if (_locations.Any(l => l.Code == location.Code))
        {
            throw new DomainException($"Location {location.Code} already exists in zone {Code}.");
        }

        _locations.Add(location);
        return location;
    }
}
