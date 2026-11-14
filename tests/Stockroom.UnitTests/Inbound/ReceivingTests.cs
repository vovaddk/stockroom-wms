using Stockroom.Domain.Common;
using Stockroom.Domain.Inbound;
using static Stockroom.UnitTests.TestWarehouse;

namespace Stockroom.UnitTests.Inbound;

public class ReceivingTests
{
    private readonly Domain.Catalog.Product _apple = Product("APPLE");
    private readonly Domain.Catalog.Product _pear = Product("PEAR");

    [Fact]
    public void Partial_receipts_accumulate_and_asn_closes_when_all_lines_complete()
    {
        var asn = NewAsn();

        asn.Receive(_apple, 4);
        asn.Receive(_apple, 6);
        Assert.Equal(AsnStatus.Open, asn.Status);

        var line = asn.Receive(_pear, 5);

        Assert.Equal(5, line.ReceivedQuantity);
        Assert.Equal(AsnStatus.Closed, asn.Status);
    }

    [Fact]
    public void Over_receipt_is_rejected_and_line_is_unchanged()
    {
        var asn = NewAsn();
        asn.Receive(_apple, 8);

        var ex = Assert.Throws<DomainException>(() => asn.Receive(_apple, 3));

        Assert.Contains("Over-receipt", ex.Message);
        Assert.Equal(8, asn.Lines.Single(l => l.Product == _apple).ReceivedQuantity);
    }

    [Fact]
    public void Product_not_on_asn_is_rejected()
    {
        Assert.Throws<DomainException>(() => NewAsn().Receive(Product("BANANA"), 1));
    }

    [Fact]
    public void Receiving_against_closed_asn_is_rejected()
    {
        var asn = NewAsn();
        asn.Receive(_apple, 10);
        asn.Receive(_pear, 5);

        Assert.Throws<DomainException>(() => asn.Receive(_apple, 1));
    }

    [Fact]
    public void Duplicate_lines_are_rejected()
    {
        Assert.Throws<DomainException>(() => NewAsn().AddLine(_apple, 1));
    }

    [Fact]
    public void Existing_lot_with_different_expiry_is_rejected()
    {
        var lot = Lot(_apple, "L1", Today.AddDays(30));

        lot.EnsureExpiryMatches(null);
        lot.EnsureExpiryMatches(Today.AddDays(30));
        Assert.Throws<DomainException>(() => lot.EnsureExpiryMatches(Today.AddDays(31)));
    }

    [Fact]
    public void Put_away_must_go_to_suggested_bin_and_completes_once()
    {
        var wh = new TestWarehouse();
        var suggested = wh.ZoneA.AddLocation("A-01-01-1", 1, 1, 100);
        var other = wh.ZoneA.AddLocation("A-01-02-1", 1, 2, 100);
        var task = new PutAwayTask(Lot(_apple), wh.Dock, suggested, 5, Now);

        Assert.Throws<DomainException>(() => task.Complete(other, "op", Now));
        task.Complete(suggested, "op", Now);

        Assert.Equal(PutAwayTaskStatus.Completed, task.Status);
        Assert.Throws<InvalidStateTransitionException>(() => task.Complete(suggested, "op", Now));
    }

    [Fact]
    public void Put_away_without_suggestion_accepts_any_storage_bin_but_not_the_dock()
    {
        var wh = new TestWarehouse();
        var task = new PutAwayTask(Lot(_apple), wh.Dock, null, 5, Now);

        Assert.Throws<DomainException>(() => task.Complete(wh.Dock, "op", Now));
        task.Complete(wh.ZoneB.AddLocation("B-01-01-1", 1, 1, 100), "op", Now);

        Assert.Equal("B-01-01-1", task.ActualLocation!.Code);
    }

    private Asn NewAsn()
    {
        var asn = new Asn("ASN-1", "Supplier", Now);
        asn.AddLine(_apple, 10);
        asn.AddLine(_pear, 5);
        return asn;
    }
}
