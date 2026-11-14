using Stockroom.Domain.Topology;

namespace Stockroom.Domain.PutAway;

/// <param name="LotId">Lot being stored; used to consolidate with the same SKU + lot.</param>
/// <param name="AbcClass">Velocity class of the product.</param>
/// <param name="RequiredVolume">Litres needed for the whole quantity.</param>
/// <param name="Origin">Where the goods are now (the dock); "nearest" is measured from here.</param>
public sealed record PutAwayRequest(int LotId, AbcClass AbcClass, decimal RequiredVolume, Location Origin);

/// <summary>Current state of a candidate bin: stock already there plus put-aways already heading to it.</summary>
public sealed record BinSnapshot(Location Location, decimal UsedVolume, IReadOnlySet<int> LotIds)
{
    public bool IsEmpty => LotIds.Count == 0;

    public bool CanFit(decimal volume) => !Location.IsBlocked && Location.MaxVolume - UsedVolume >= volume;
}

public interface IPutAwayStrategy
{
    /// <summary>Name used to order strategies in configuration.</summary>
    string Name { get; }

    /// <returns>The chosen bin, or <c>null</c> when this strategy has no answer.</returns>
    Location? Suggest(PutAwayRequest request, IReadOnlyCollection<BinSnapshot> bins);
}

/// <summary>1. Top up a bin that already holds the same lot, so one lot is not scattered over the warehouse.</summary>
public sealed class ConsolidateSameLotStrategy : IPutAwayStrategy
{
    public string Name => "ConsolidateSameLot";

    public Location? Suggest(PutAwayRequest request, IReadOnlyCollection<BinSnapshot> bins) =>
        bins.Where(b => b.LotIds.Contains(request.LotId) && b.CanFit(request.RequiredVolume)).Nearest(request.Origin);
}

/// <summary>2. Nearest empty bin in a zone whose ABC class matches the product: fast movers stay close to the dock.</summary>
public sealed class EmptyBinInAbcZoneStrategy : IPutAwayStrategy
{
    public string Name => "EmptyBinInAbcZone";

    public Location? Suggest(PutAwayRequest request, IReadOnlyCollection<BinSnapshot> bins) =>
        bins.Where(b => b.IsEmpty && b.Location.Zone.AbcClass == request.AbcClass && b.CanFit(request.RequiredVolume))
            .Nearest(request.Origin);
}

/// <summary>3. Fallback: nearest bin anywhere with enough free volume.</summary>
public sealed class AnyBinWithCapacityStrategy : IPutAwayStrategy
{
    public string Name => "AnyBinWithCapacity";

    public Location? Suggest(PutAwayRequest request, IReadOnlyCollection<BinSnapshot> bins) =>
        bins.Where(b => b.CanFit(request.RequiredVolume)).Nearest(request.Origin);
}

/// <summary>Chain of responsibility: the first strategy that returns a bin wins.</summary>
public sealed class PutAwayStrategyChain(IEnumerable<IPutAwayStrategy> strategies) : IPutAwayStrategy
{
    private readonly IReadOnlyList<IPutAwayStrategy> _strategies = [.. strategies];

    public string Name => string.Join(" > ", _strategies.Select(s => s.Name));

    public Location? Suggest(PutAwayRequest request, IReadOnlyCollection<BinSnapshot> bins) =>
        _strategies.Select(s => s.Suggest(request, bins)).FirstOrDefault(l => l is not null);
}

internal static class BinSnapshotExtensions
{
    // Code as tie-breaker keeps suggestions deterministic.
    public static Location? Nearest(this IEnumerable<BinSnapshot> bins, Location origin) =>
        bins.Select(b => b.Location)
            .OrderBy(l => l.DistanceTo(origin))
            .ThenBy(l => l.Code, StringComparer.Ordinal)
            .FirstOrDefault();
}
