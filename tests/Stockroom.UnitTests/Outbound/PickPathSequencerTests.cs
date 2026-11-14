using Stockroom.Domain.Outbound;

namespace Stockroom.UnitTests.Outbound;

public class PickPathSequencerTests
{
    [Fact]
    public void Route_snakes_up_first_aisle_and_down_the_next()
    {
        var wh = new TestWarehouse();
        var bins = new[]
        {
            wh.ZoneA.AddLocation("A-02-01-1", 2, 1, 10),
            wh.ZoneA.AddLocation("A-01-03-1", 1, 3, 10),
            wh.ZoneA.AddLocation("A-03-02-1", 3, 2, 10),
            wh.ZoneA.AddLocation("A-02-04-1", 2, 4, 10),
            wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 10),
            wh.ZoneA.AddLocation("A-03-05-1", 3, 5, 10),
        };

        var route = PickPathSequencer.Sequence(bins, b => b);

        Assert.Equal(
            ["A-01-01-1", "A-01-03-1", "A-02-04-1", "A-02-01-1", "A-03-02-1", "A-03-05-1"],
            route.Select(b => b.Code));
    }
}
