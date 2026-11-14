using Stockroom.Domain.Common;

namespace Stockroom.Domain.Users;

public static class Roles
{
    /// <summary>Terminal user: receiving, put-away, picking, transfers.</summary>
    public const string Operator = "Operator";

    /// <summary>Master data, documents, allocation, adjustments; can also do everything an operator does.</summary>
    public const string Manager = "Manager";

    public static bool IsValid(string role) => role is Operator or Manager;
}

public sealed class User
{
    private User() { }

    public User(string userName, string role)
    {
        UserName = Guard.NotBlank(userName, nameof(UserName)).ToLowerInvariant();
        Role = Roles.IsValid(role) ? role : throw new DomainException($"Unknown role '{role}'.");
    }

    public int Id { get; private set; }
    public string UserName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = null!;

    public void SetPasswordHash(string hash) => PasswordHash = Guard.NotBlank(hash, nameof(hash));
}
