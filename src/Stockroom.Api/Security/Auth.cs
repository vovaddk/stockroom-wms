using System.Globalization;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Stockroom.Application.Abstractions;
using Stockroom.Domain.Users;

namespace Stockroom.Api.Security;

public static class Policies
{
    /// <summary>Terminal actions. Managers can do them too.</summary>
    public const string Operator = "Operator";

    public const string Manager = "Manager";
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "stockroom";
    public string Audience { get; set; } = "stockroom";
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>One shift by default.</summary>
    public int LifetimeMinutes { get; set; } = 480;

    public SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = Key,
        NameClaimType = JwtRegisteredClaimNames.Name,
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(jwt.LifetimeMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(CultureInfo.InvariantCulture),
                [JwtRegisteredClaimNames.Name] = user.UserName,
                ["role"] = user.Role,
            },
            SigningCredentials = new SigningCredentials(jwt.Key, SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserName => accessor.HttpContext?.User.Identity?.Name
        ?? throw new InvalidOperationException("No authenticated user in the current request.");
}
