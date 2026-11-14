using System.Text.RegularExpressions;
using Stockroom.Domain.Common;

namespace Stockroom.Domain.Topology;

/// <summary>
/// A bin. Storage codes follow aisle-bay-level-position (e.g. <c>A-01-03-2</c>);
/// X is the aisle, Y the position along it, used for walking distance.
/// </summary>
public sealed partial class Location
{
    private Location() { }

    internal Location(Zone zone, string code, int x, int y, decimal maxVolume)
    {
        Code = Guard.Code(code, nameof(Code));
        if (!CodePattern().IsMatch(Code))
        {
            throw new DomainException($"Location code '{Code}' is invalid. Use letters/digits separated by '-', e.g. A-01-03-2.");
        }

        Zone = zone;
        X = x;
        Y = y;
        MaxVolume = Guard.Positive(maxVolume, nameof(MaxVolume));
    }

    public int Id { get; private set; }
    public int ZoneId { get; private set; }
    public Zone Zone { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public int X { get; private set; }
    public int Y { get; private set; }

    /// <summary>Capacity in litres.</summary>
    public decimal MaxVolume { get; private set; }

    public bool IsBlocked { get; private set; }

    public void Block() => IsBlocked = true;

    public void Unblock() => IsBlocked = false;

    /// <summary>Manhattan distance: people walk along aisles, not diagonally through racks.</summary>
    public int DistanceTo(Location other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public void EnsureUsable()
    {
        if (IsBlocked)
        {
            throw new DomainException($"Location {Code} is blocked.");
        }
    }

    public void EnsureCanFit(decimal usedVolume, decimal extraVolume)
    {
        if (usedVolume + extraVolume > MaxVolume)
        {
            throw new DomainException(
                $"Location {Code} cannot fit {extraVolume:0.###} l: {MaxVolume - usedVolume:0.###} l of {MaxVolume:0.###} l free.");
        }
    }

    [GeneratedRegex("^[A-Z0-9]+(-[A-Z0-9]+)*$")]
    private static partial Regex CodePattern();
}
