using FluentValidation;
using Stockroom.Application.Abstractions;
using Stockroom.Application.Common;
using Stockroom.Application.Reports;
using Stockroom.Domain.Common;
using Stockroom.Domain.Inventory;

namespace Stockroom.Application.Inventory;

public sealed record TransferRequest(string FromLocationCode, string ToLocationCode, string Barcode, string LotNumber, int Quantity);

public sealed record AdjustmentRequest(string LocationCode, string Barcode, string LotNumber, int CountedQuantity, string Reason);

/// <param name="Movement"><c>null</c> when the count matched the system quantity.</param>
public sealed record AdjustmentResult(int PreviousQuantity, int CountedQuantity, MovementDto? Movement);

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.FromLocationCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ToLocationCode).NotEmpty().MaximumLength(32)
            .NotEqual(x => x.FromLocationCode, StringComparer.OrdinalIgnoreCase).WithMessage("Target must differ from source.");
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.LotNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class AdjustmentRequestValidator : AbstractValidator<AdjustmentRequest>
{
    public AdjustmentRequestValidator()
    {
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.LotNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class InventoryService(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task<MovementDto> TransferAsync(TransferRequest request, CancellationToken ct)
    {
        var product = await db.GetProductByBarcodeAsync(request.Barcode, ct);
        var lot = await db.GetLotAsync(product, request.LotNumber, ct);
        var from = await db.GetLocationAsync(request.FromLocationCode, ct);
        var to = await db.GetLocationAsync(request.ToLocationCode, ct);
        if (!to.Zone.IsStorage)
        {
            throw new DomainException($"{to.Code} is not a storage location.");
        }

        to.EnsureCanFit(await db.GetUsedVolumeAsync(to, ct), product.VolumeOf(request.Quantity));
        var source = await db.GetOrAddStockItemAsync(from, lot, ct);
        var target = await db.GetOrAddStockItemAsync(to, lot, ct);

        var context = new MovementContext(user.UserName, $"TR-{from.Code}>{to.Code}", clock.GetUtcNow());
        var movement = StockLedger.Move(StockMovementType.Transfer, source, target, request.Quantity, context);
        db.StockMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        return MovementDto.From(movement);
    }

    /// <summary>Books a physical count: the difference to the system quantity becomes an Adjustment movement.</summary>
    public async Task<AdjustmentResult> AdjustAsync(AdjustmentRequest request, CancellationToken ct)
    {
        var product = await db.GetProductByBarcodeAsync(request.Barcode, ct);
        var lot = await db.GetLotAsync(product, request.LotNumber, ct);
        var location = await db.GetLocationAsync(request.LocationCode, ct);
        var stock = await db.GetOrAddStockItemAsync(location, lot, ct);
        var previous = stock.Quantity;

        var context = new MovementContext(user.UserName, $"COUNT-{location.Code}", clock.GetUtcNow());
        var movement = StockLedger.Adjust(stock, request.CountedQuantity, request.Reason, context);
        if (movement is not null)
        {
            db.StockMovements.Add(movement);
            await db.SaveChangesAsync(ct);
        }

        return new AdjustmentResult(previous, request.CountedQuantity, movement is null ? null : MovementDto.From(movement));
    }
}
