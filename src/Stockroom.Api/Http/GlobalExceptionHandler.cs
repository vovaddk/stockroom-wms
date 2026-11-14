using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stockroom.Application.Common;
using Stockroom.Domain.Common;

namespace Stockroom.Api.Http;

/// <summary>Maps exceptions to RFC 7807 problem details. Unexpected errors are logged and never leak internals.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            InvalidStateTransitionException => (StatusCodes.Status409Conflict, "Invalid state", exception.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update",
                "The data was changed by someone else at the same time. Reload and try again."),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", exception.Message),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request {Method} {Path} rejected with {Status}: {Detail}",
                httpContext.Request.Method, httpContext.Request.Path, status, detail);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
