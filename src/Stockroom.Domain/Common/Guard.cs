namespace Stockroom.Domain.Common;

internal static class Guard
{
    public static int Positive(int value, string name) =>
        value > 0 ? value : throw new DomainException($"{name} must be greater than zero.");

    public static decimal Positive(decimal value, string name) =>
        value > 0 ? value : throw new DomainException($"{name} must be greater than zero.");

    public static string NotBlank(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{name} is required.") : value.Trim();

    public static string Code(string? value, string name) => NotBlank(value, name).ToUpperInvariant();
}
