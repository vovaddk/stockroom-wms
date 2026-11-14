using FluentValidation;
using Stockroom.Domain.Outbound;

namespace Stockroom.Application.Outbound;

public sealed record CreateOrderRequest(string Number, string Customer, IReadOnlyList<CreateOrderLineRequest> Lines);

public sealed record CreateOrderLineRequest(string Sku, int Quantity);

public sealed record OrderSummaryDto(int Id, string Number, string Customer, OrderStatus Status, DateTimeOffset CreatedAt, int LineCount);

public sealed record OrderDto(
    int Id,
    string Number,
    string Customer,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ShippedAt,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<PickTaskDto> PickTasks);

public sealed record OrderLineDto(int Id, string Sku, string ProductName, int Quantity, int PickedQuantity);

public sealed record PickTaskDto(
    int Id,
    int OrderId,
    string OrderNumber,
    int Sequence,
    string LocationCode,
    string Sku,
    string Barcode,
    string ProductName,
    string LotNumber,
    DateOnly? ExpiryDate,
    int Quantity,
    int PickedQuantity,
    PickTaskStatus Status);

public sealed record ConfirmPickRequest(string LocationCode, string Barcode, int Quantity);

public sealed record PickConfirmation(int TaskId, PickTaskStatus Status, int PickedQuantity, string OrderNumber, OrderStatus OrderStatus);

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Number).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Customer).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.Sku.Trim().ToUpperInvariant()).Distinct().Count() == lines.Count)
            .When(x => x.Lines is { Count: > 0 } && x.Lines.All(l => l.Sku is not null))
            .WithMessage("Each SKU may appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ConfirmPickRequestValidator : AbstractValidator<ConfirmPickRequest>
{
    public ConfirmPickRequestValidator()
    {
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
    }
}

internal static class OutboundMapping
{
    public static OrderDto ToDto(this Order o) => new(
        o.Id, o.Number, o.Customer, o.Status, o.CreatedAt, o.ShippedAt,
        [.. o.Lines.Select(l => new OrderLineDto(l.Id, l.Product.Sku, l.Product.Name, l.Quantity, l.PickedQuantity))],
        [.. o.PickTasks.OrderBy(t => t.Sequence).Select(t => t.ToDto())]);

    public static PickTaskDto ToDto(this PickTask t) => new(
        t.Id, t.Order.Id, t.Order.Number, t.Sequence, t.StockItem.Location.Code,
        t.StockItem.Product.Sku, t.StockItem.Product.Barcode, t.StockItem.Product.Name,
        t.StockItem.Lot.LotNumber, t.StockItem.Lot.ExpiryDate, t.Quantity, t.PickedQuantity, t.Status);
}
