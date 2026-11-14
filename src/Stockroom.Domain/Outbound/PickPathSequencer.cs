using Stockroom.Domain.Topology;

namespace Stockroom.Domain.Outbound;

/// <summary>
/// Serpentine (snake) walking route: aisles in ascending X; even-numbered passes walk up the aisle (ascending Y),
/// odd passes walk back down, so the picker never walks an aisle twice.
/// </summary>
public static class PickPathSequencer
{
    public static IReadOnlyList<T> Sequence<T>(IEnumerable<T> items, Func<T, Location> locationOf) =>
        items.GroupBy(i => locationOf(i).X)
            .OrderBy(aisle => aisle.Key)
            .SelectMany((aisle, pass) => pass % 2 == 0
                ? aisle.OrderBy(i => locationOf(i).Y).ThenBy(i => locationOf(i).Code, StringComparer.Ordinal)
                : aisle.OrderByDescending(i => locationOf(i).Y).ThenBy(i => locationOf(i).Code, StringComparer.Ordinal))
            .ToList();
}
