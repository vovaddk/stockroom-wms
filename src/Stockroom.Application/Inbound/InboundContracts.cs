using FluentValidation;
using Stockroom.Domain.Inbound;

namespace Stockroom.Application.Inbound;

public sealed record CreateAsnRequest(string Number, string Supplier, IReadOnlyList<CreateAsnLineRequest> Lines);

public sealed record CreateAsnLineRequest(string Sku, int ExpectedQuantity);

public sealed record AsnDto(int Id, string Number, string Supplier, AsnStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<AsnLineDto> Lines);

public sealed record AsnLineDto(int Id, string Sku, string Barcode, string ProductName, int ExpectedQuantity, int ReceivedQuantity);

/// <param name="LotNumber">Optional; defaults to the ASN number.</param>
public sealed record ReceiveRequest(string Barcode, int Quantity, string? LotNumber, DateOnly? ExpiryDate);

/// <param name="ReceivedQuantity">Quantity booked by this receipt.</param>
/// <param name="RemainingQuantity">Still expected on the ASN line after this receipt.</param>
public sealed record ReceiptResult(
    int AsnId,
    string Sku,
    string LotNumber,
    int ReceivedQuantity,
    int RemainingQuantity,
    string ReceivingLocationCode,
    int PutAwayTaskId,
    string? SuggestedLocationCode);

public sealed record PutAwayTaskDto(
    int Id,
    string Sku,
    string Barcode,
    string ProductName,
    string LotNumber,
    int Quantity,
    string FromLocationCode,
    string? SuggestedLocationCode,
    string? ActualLocationCode,
    PutAwayTaskStatus Status,
    DateTimeOffset CreatedAt);

public sealed record ConfirmPutAwayRequest(string LocationCode);

public sealed class CreateAsnRequestValidator : AbstractValidator<CreateAsnRequest>
{
    public CreateAsnRequestValidator()
    {
        RuleFor(x => x.Number).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Supplier).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.Sku.Trim().ToUpperInvariant()).Distinct().Count() == lines.Count)
            .When(x => x.Lines is { Count: > 0 } && x.Lines.All(l => l.Sku is not null))
            .WithMessage("Each SKU may appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku).NotEmpty();
            line.RuleFor(l => l.ExpectedQuantity).GreaterThan(0);
        });
    }
}

public sealed class ReceiveRequestValidator : AbstractValidator<ReceiveRequest>
{
    public ReceiveRequestValidator()
    {
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(100_000);
        RuleFor(x => x.LotNumber).MaximumLength(32).Matches("^[A-Za-z0-9-]*$");
    }
}

public sealed class ConfirmPutAwayRequestValidator : AbstractValidator<ConfirmPutAwayRequest>
{
    public ConfirmPutAwayRequestValidator() => RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(32);
}

internal static class InboundMapping
{
    public static AsnDto ToDto(this Asn asn) => new(
        asn.Id, asn.Number, asn.Supplier, asn.Status, asn.CreatedAt,
        [.. asn.Lines.Select(l => new AsnLineDto(l.Id, l.Product.Sku, l.Product.Barcode, l.Product.Name, l.ExpectedQuantity, l.ReceivedQuantity))]);

    public static PutAwayTaskDto ToDto(this PutAwayTask t) => new(
        t.Id, t.Product.Sku, t.Product.Barcode, t.Product.Name, t.Lot.LotNumber, t.Quantity,
        t.FromLocation.Code, t.SuggestedLocation?.Code, t.ActualLocation?.Code, t.Status, t.CreatedAt);
}
