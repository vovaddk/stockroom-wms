using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Stockroom.Api.Security;

/// <summary>Slows down password guessing: a fixed number of login attempts per client IP per minute.</summary>
internal static class LoginRateLimit
{
    public const string Policy = "login";

    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var permitsPerMinute = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Policy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
    }
}
