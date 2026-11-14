using Stockroom.Domain.Common;

namespace Stockroom.Domain.Topology;

public sealed class Warehouse
{
    private readonly List<Zone> _zones = [];

    private Warehouse() { }

    public Warehouse(string code, string name)
    {
        Code = Guard.Code(code, nameof(Code));
        Name = Guard.NotBlank(name, nameof(Name));
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public IReadOnlyCollection<Zone> Zones => _zones;

    public Zone AddZone(string code, string name, ZoneType type, AbcClass abcClass)
    {
        var zone = new Zone(this, code, name, type, abcClass);
        if (_zones.Any(z => z.Code == zone.Code))
        {
            throw new DomainException($"Zone {zone.Code} already exists in warehouse {Code}.");
        }

        _zones.Add(zone);
        return zone;
    }
}
