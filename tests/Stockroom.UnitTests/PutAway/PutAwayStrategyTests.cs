using Stockroom.Domain.PutAway;
using Stockroom.Domain.Topology;

namespace Stockroom.UnitTests.PutAway;

public class PutAwayStrategyTests
{
    private const int LotId = 7;
    private readonly TestWarehouse _wh = new();

    private PutAwayRequest Request(decimal volume = 10, AbcClass abc = AbcClass.A) => new(LotId, abc, volume, _wh.Dock);

    private static BinSnapshot Empty(Location location) => new(location, 0, new HashSet<int>());

    private static BinSnapshot Holding(Location location, decimal used, params int[] lotIds) => new(location, used, lotIds.ToHashSet());

    [Fact]
    public void Consolidate_picks_nearest_bin_with_same_lot_and_room()
    {
        var far = Holding(_wh.ZoneB.AddLocation("B-05-01-1", 5, 1, 100), 20, LotId);
        var near = Holding(_wh.ZoneB.AddLocation("B-02-01-1", 2, 1, 100), 20, LotId);
        var otherLot = Holding(_wh.ZoneA.AddLocation("A-01-01-1", 1, 0, 100), 0, 99);

        var result = new ConsolidateSameLotStrategy().Suggest(Request(), [far, near, otherLot]);

        Assert.Same(near.Location, result);
    }

    [Fact]
    public void Consolidate_skips_bin_with_same_lot_that_is_too_full()
    {
        var full = Holding(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), 95, LotId);

        Assert.Null(new ConsolidateSameLotStrategy().Suggest(Request(volume: 10), [full]));
    }

    [Fact]
    public void Abc_strategy_picks_nearest_empty_bin_in_matching_zone()
    {
        var bClassNear = Empty(_wh.ZoneB.AddLocation("B-01-01-1", 1, 0, 100));
        var aClassFar = Empty(_wh.ZoneA.AddLocation("A-03-01-1", 3, 1, 100));
        var aClassNear = Empty(_wh.ZoneA.AddLocation("A-02-01-1", 2, 1, 100));
        var aClassOccupied = Holding(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100), 5, 42);

        var result = new EmptyBinInAbcZoneStrategy().Suggest(Request(abc: AbcClass.A), [bClassNear, aClassFar, aClassNear, aClassOccupied]);

        Assert.Same(aClassNear.Location, result);
    }

    [Fact]
    public void Blocked_and_too_small_bins_are_never_suggested()
    {
        var blocked = _wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100);
        blocked.Block();
        var tiny = _wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 5);

        Assert.Null(new AnyBinWithCapacityStrategy().Suggest(Request(volume: 10), [Empty(blocked), Empty(tiny)]));
    }

    [Fact]
    public void Fallback_returns_nearest_bin_with_capacity_in_any_zone()
    {
        var partlyUsedB = Holding(_wh.ZoneB.AddLocation("B-01-01-1", 1, 1, 100), 50, 42);
        var farA = Empty(_wh.ZoneA.AddLocation("A-09-01-1", 9, 1, 100));

        Assert.Same(partlyUsedB.Location, new AnyBinWithCapacityStrategy().Suggest(Request(), [farA, partlyUsedB]));
    }

    [Fact]
    public void Chain_uses_first_strategy_that_answers()
    {
        var sameLotFar = Holding(_wh.ZoneB.AddLocation("B-08-01-1", 8, 1, 100), 10, LotId);
        var emptyANear = Empty(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100));
        IReadOnlyCollection<BinSnapshot> bins = [sameLotFar, emptyANear];

        var consolidateFirst = new PutAwayStrategyChain([new ConsolidateSameLotStrategy(), new EmptyBinInAbcZoneStrategy()]);
        var abcFirst = new PutAwayStrategyChain([new EmptyBinInAbcZoneStrategy(), new ConsolidateSameLotStrategy()]);

        Assert.Same(sameLotFar.Location, consolidateFirst.Suggest(Request(), bins));
        Assert.Same(emptyANear.Location, abcFirst.Suggest(Request(), bins));
    }

    [Fact]
    public void Chain_falls_through_to_fallback_when_no_lot_match_and_no_empty_abc_bin()
    {
        var partlyUsedB = Holding(_wh.ZoneB.AddLocation("B-01-01-1", 1, 1, 100), 30, 42);
        var chain = new PutAwayStrategyChain(
            [new ConsolidateSameLotStrategy(), new EmptyBinInAbcZoneStrategy(), new AnyBinWithCapacityStrategy()]);

        Assert.Same(partlyUsedB.Location, chain.Suggest(Request(abc: AbcClass.A), [partlyUsedB]));
    }

    [Fact]
    public void Chain_returns_null_when_nothing_fits()
    {
        var chain = new PutAwayStrategyChain([new ConsolidateSameLotStrategy(), new AnyBinWithCapacityStrategy()]);

        Assert.Null(chain.Suggest(Request(volume: 1_000), [Empty(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100))]));
    }

    [Fact]
    public void Equidistant_bins_are_resolved_by_code_for_deterministic_suggestions()
    {
        var second = Empty(_wh.ZoneA.AddLocation("A-01-02-1", 1, 1, 100));
        var first = Empty(_wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100));

        Assert.Same(first.Location, new AnyBinWithCapacityStrategy().Suggest(Request(), [second, first]));
    }
}
