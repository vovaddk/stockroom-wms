using Microsoft.AspNetCore.Http.HttpResults;
using Stockroom.Api.Http;
using Stockroom.Api.Security;
using Stockroom.Application.Auth;

namespace Stockroom.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync)
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimit.Policy)
            .Validate<LoginRequest>();
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, AuthService auth, CancellationToken ct)
    {
        var response = await auth.LoginAsync(request, ct);
        return response is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid user name or password.")
            : TypedResults.Ok(response);
    }
}
