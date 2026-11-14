using FluentValidation;

namespace Stockroom.Api.Http;

/// <summary>Runs the FluentValidation validator for the <typeparamref name="T"/> argument; failures become a 400 ValidationProblem.</summary>
internal sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : TypedResults.ValidationProblem(result.ToDictionary());
    }
}

internal static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder)
        where T : class => builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
