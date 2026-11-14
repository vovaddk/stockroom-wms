using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.PutAway;
using Stockroom.Domain.Topology;

namespace Stockroom.Application.Inbound;

/// <summary>Builds bin snapshots from the database and asks the configured strategy chain for a bin.</summary>
public sealed class PutAwayPlanner(IAppDbContext db, IPutAwayStrategy strategy)
{
    public async Task<Location?> SuggestAsync(Lot lot, int quantity, Location origin, CancellationToken ct)
    {
        // shortcut: loads every usable storage bin into memory; fine for a few thousand bins, push the filtering into SQL beyond that.
        var bins = await db.Locations
            .Include(l => l.Zone)
            .Where(l => l.Zone.WarehouseId == origin.Zone.WarehouseId
                && !l.IsBlocked
                && (l.Zone.Type == ZoneType.Storage || l.Zone.Type == ZoneType.Picking))
            .ToListAsync(ct);

        var stored = await db.StockItems
            .Where(s => s.Quantity > 0)
            .Select(s => new { s.LocationId, s.LotId, Volume = s.Quantity * s.Product.UnitVolume })
            .ToListAsync(ct);

        // Open put-aways already heading to a bin count as occupied, so two pallets are not sent to the same empty bin.
        var incoming = await db.PutAwayTasks
            .Where(t => t.Status == PutAwayTaskStatus.Open && t.SuggestedLocationId != null)
            .Select(t => new { LocationId = t.SuggestedLocationId!.Value, t.LotId, Volume = t.Quantity * t.Product.UnitVolume })
            .ToListAsync(ct);

        var usage = stored.Concat(incoming).ToLookup(u => u.LocationId);
        var snapshots = bins
            .Select(b => new BinSnapshot(b, usage[b.Id].Sum(u => u.Volume), usage[b.Id].Select(u => u.LotId).ToHashSet()))
            .ToList();

        var request = new PutAwayRequest(lot.Id, lot.Product.AbcClass, lot.Product.VolumeOf(quantity), origin);
        return strategy.Suggest(request, snapshots);
    }
}
