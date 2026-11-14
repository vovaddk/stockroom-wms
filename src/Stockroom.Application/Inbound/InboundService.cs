using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Application.Common;
using Stockroom.Domain.Catalog;
using Stockroom.Domain.Common;
using Stockroom.Domain.Inbound;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Topology;

namespace Stockroom.Application.Inbound;

public sealed class InboundService(IAppDbContext db, PutAwayPlanner planner, ICurrentUser user, TimeProvider clock)
{
    public async Task<AsnDto> CreateAsnAsync(CreateAsnRequest request, CancellationToken ct)
    {
        var asn = new Asn(request.Number, request.Supplier, clock.GetUtcNow());
        if (await db.Asns.AnyAsync(a => a.Number == asn.Number, ct))
        {
            throw new ConflictException($"ASN {asn.Number} already exists.");
        }

        var skus = request.Lines.Select(l => l.Sku.Trim().ToUpperInvariant()).ToList();
        var products = await db.Products.Where(p => skus.Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, ct);
        foreach (var line in request.Lines)
        {
            var sku = line.Sku.Trim().ToUpperInvariant();
            var product = products.GetValueOrDefault(sku) ?? throw new DomainException($"Unknown SKU {sku}.");
            asn.AddLine(product, line.ExpectedQuantity);
        }

        db.Asns.Add(asn);
        await db.SaveChangesAsync(ct);
        return asn.ToDto();
    }

    public async Task<AsnDto> GetAsnAsync(int id, CancellationToken ct) => (await LoadAsnAsync(id, ct)).ToDto();

    public async Task<PagedResult<AsnDto>> ListAsnsAsync(AsnStatus? status, PageQuery page, CancellationToken ct)
    {
        var query = db.Asns.AsNoTracking().Include(a => a.Lines).ThenInclude(l => l.Product).AsQueryable();
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        var result = await query.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToPagedResultAsync(page, ct);
        return result.Map(a => a.ToDto());
    }

    /// <summary>
    /// Books goods onto the receiving dock (ledger: Receipt) and creates a put-away task with a suggested bin.
    /// Stock, ASN progress, ledger entry and task are saved in one transaction.
    /// </summary>
    public async Task<ReceiptResult> ReceiveAsync(int asnId, ReceiveRequest request, CancellationToken ct)
    {
        var asn = await LoadAsnAsync(asnId, ct);
        var product = await db.GetProductByBarcodeAsync(request.Barcode, ct);
        var line = asn.Receive(product, request.Quantity);

        var now = clock.GetUtcNow();
        var lotNumber = string.IsNullOrWhiteSpace(request.LotNumber) ? asn.Number : request.LotNumber;
        var lot = await GetOrCreateLotAsync(product, lotNumber, request.ExpiryDate, now, ct);

        var dock = await db.Locations.Include(l => l.Zone)
            .Where(l => l.Zone.Type == ZoneType.Receiving && !l.IsBlocked)
            .OrderBy(l => l.Code)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("No usable receiving location is configured.");

        var stock = await db.GetOrAddStockItemAsync(dock, lot, ct);
        db.StockMovements.Add(StockLedger.Receive(stock, request.Quantity, new MovementContext(user.UserName, asn.Number, now)));

        var suggested = await planner.SuggestAsync(lot, request.Quantity, dock, ct);
        var task = new PutAwayTask(lot, dock, suggested, request.Quantity, now);
        db.PutAwayTasks.Add(task);

        await db.SaveChangesAsync(ct);
        return new ReceiptResult(asn.Id, product.Sku, lot.LotNumber, request.Quantity, line.RemainingQuantity, dock.Code, task.Id, suggested?.Code);
    }

    public async Task<PagedResult<PutAwayTaskDto>> ListPutAwayTasksAsync(PutAwayTaskStatus? status, PageQuery page, CancellationToken ct)
    {
        var query = PutAwayTasksWithDetails().AsNoTracking();
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        var result = await query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToPagedResultAsync(page, ct);
        return result.Map(t => t.ToDto());
    }

    /// <summary>Operator scanned the bin: moves the goods from the dock into it (ledger: PutAway).</summary>
    public async Task<PutAwayTaskDto> ConfirmPutAwayAsync(int taskId, ConfirmPutAwayRequest request, CancellationToken ct)
    {
        var task = await PutAwayTasksWithDetails().FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException($"Put-away task {taskId} does not exist.");
        var target = await db.GetLocationAsync(request.LocationCode, ct);

        var now = clock.GetUtcNow();
        task.Complete(target, user.UserName, now);
        target.EnsureCanFit(await db.GetUsedVolumeAsync(target, ct), task.Product.VolumeOf(task.Quantity));

        var source = await db.GetOrAddStockItemAsync(task.FromLocation, task.Lot, ct);
        var destination = await db.GetOrAddStockItemAsync(target, task.Lot, ct);
        var context = new MovementContext(user.UserName, $"PA-{task.Id}", now);
        db.StockMovements.Add(StockLedger.Move(StockMovementType.PutAway, source, destination, task.Quantity, context));

        await db.SaveChangesAsync(ct);
        return task.ToDto();
    }

    private async Task<Asn> LoadAsnAsync(int id, CancellationToken ct) =>
        await db.Asns.Include(a => a.Lines).ThenInclude(l => l.Product).FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException($"ASN {id} does not exist.");

    private IQueryable<PutAwayTask> PutAwayTasksWithDetails() => db.PutAwayTasks
        .Include(t => t.Product)
        .Include(t => t.Lot)
        .Include(t => t.FromLocation)
        .Include(t => t.SuggestedLocation)
        .Include(t => t.ActualLocation);

    private async Task<Lot> GetOrCreateLotAsync(
        Product product, string lotNumber, DateOnly? expiry, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = lotNumber.Trim().ToUpperInvariant();
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.LotNumber == normalized, ct);
        if (lot is null)
        {
            lot = new Lot(product, normalized, expiry, now);
            db.Lots.Add(lot);
        }

        lot.EnsureExpiryMatches(expiry);
        return lot;
    }
}
