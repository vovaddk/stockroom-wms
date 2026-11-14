using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Users;

namespace Stockroom.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string UserName, string Role);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class AuthService(IAppDbContext db, IPasswordHasher<User> hasher, ITokenService tokens)
{
    /// <returns><c>null</c> for an unknown user or wrong password (the caller must not reveal which).</returns>
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);
        if (user is null)
        {
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, request.Password));
            await db.SaveChangesAsync(ct);
        }

        var token = tokens.Create(user);
        return new LoginResponse(token.Token, token.ExpiresAt, user.UserName, user.Role);
    }
}
