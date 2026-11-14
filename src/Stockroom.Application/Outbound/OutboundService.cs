using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Application.Common;
using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;
using Stockroom.Domain.Outbound;

namespace Stockroom.Application.Outbound;

public sealed class OutboundService(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var order = new Order(request.Number, request.Customer, clock.GetUtcNow());
        if (await db.Orders.AnyAsync(o => o.Number == order.Number, ct))
        {
            throw new ConflictException($"Order {order.Number} already exists.");
        }

        var skus = request.Lines.Select(l => l.Sku.Trim().ToUpperInvariant()).ToList();
        var products = await db.Products.Where(p => skus.Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, ct);
        foreach (var line in request.Lines)
        {
            var sku = line.Sku.Trim().ToUpperInvariant();
            order.AddLine(products.GetValueOrDefault(sku) ?? throw new DomainException($"Unknown SKU {sku}."), line.Quantity);
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.ToDto();
    }

    public async Task<OrderDto> GetOrderAsync(int id, CancellationToken ct) => (await LoadOrderAsync(id, ct)).ToDto();

    public Task<PagedResult<OrderSummaryDto>> ListOrdersAsync(OrderStatus? status, PageQuery page, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        return query.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .Select(o => new OrderSummaryDto(o.Id, o.Number, o.Customer, o.Status, o.CreatedAt, o.Lines.Count))
            .ToPagedResultAsync(page, ct);
    }

    /// <summary>Reserves stock FEFO and creates pick tasks in walking order.</summary>
    public async Task<OrderDto> AllocateAsync(int id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, ct);
        var productIds = order.Lines.Select(l => l.ProductId).ToList();
        var candidates = await db.StockItems
            .Include(s => s.Product)
            .Include(s => s.Lot)
            .Include(s => s.Location).ThenInclude(l => l.Zone)
            .Where(s => productIds.Contains(s.ProductId) && s.Quantity > s.ReservedQuantity)
            .ToListAsync(ct);

        order.Allocate(candidates, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return order.ToDto();
    }

    /// <summary>Next open task in walking sequence, oldest order first; <c>null</c> when there is nothing to pick.</summary>
    public async Task<PickTaskDto?> GetNextPickTaskAsync(int? orderId, CancellationToken ct)
    {
        var task = await PickTasksWithDetails().AsNoTracking()
            .Where(t => t.Status == PickTaskStatus.Open && (orderId == null || t.OrderId == orderId))
            .OrderBy(t => t.Order.CreatedAt).ThenBy(t => t.OrderId).ThenBy(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        return task?.ToDto();
    }

    /// <summary>
    /// Confirms a scanned pick. Two pickers confirming the same stock: the second save fails the rowversion check
    /// (<see cref="DbUpdateConcurrencyException"/>), which the API maps to 409.
    /// </summary>
    public async Task<PickConfirmation> ConfirmPickAsync(int taskId, ConfirmPickRequest request, CancellationToken ct)
    {
        var orderId = await db.PickTasks.Where(t => t.Id == taskId).Select(t => (int?)t.OrderId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Pick task {taskId} does not exist.");
        var order = await LoadOrderAsync(orderId, ct);
        var task = order.PickTasks.Single(t => t.Id == taskId);

        var context = new MovementContext(user.UserName, order.Number, clock.GetUtcNow());
        var movement = order.ConfirmPick(task, request.LocationCode, request.Barcode, request.Quantity, context);
        if (movement is not null)
        {
            db.StockMovements.Add(movement);
        }

        await db.SaveChangesAsync(ct);
        return new PickConfirmation(task.Id, task.Status, task.PickedQuantity, order.Number, order.Status);
    }

    public async Task<OrderDto> ShipAsync(int id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, ct);
        order.Ship(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return order.ToDto();
    }

    private async Task<Order> LoadOrderAsync(int id, CancellationToken ct) =>
        await db.Orders
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.PickTasks).ThenInclude(t => t.StockItem).ThenInclude(s => s.Location)
            .Include(o => o.PickTasks).ThenInclude(t => t.StockItem).ThenInclude(s => s.Lot)
            .Include(o => o.PickTasks).ThenInclude(t => t.StockItem).ThenInclude(s => s.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException($"Order {id} does not exist.");

    private IQueryable<PickTask> PickTasksWithDetails() => db.PickTasks
        .Include(t => t.Order)
        .Include(t => t.StockItem).ThenInclude(s => s.Location)
        .Include(t => t.StockItem).ThenInclude(s => s.Lot)
        .Include(t => t.StockItem).ThenInclude(s => s.Product);
}
