using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Stockroom.Application.Auth;
using Stockroom.Application.Inbound;
using Stockroom.Application.Inventory;
using Stockroom.Application.MasterData;
using Stockroom.Application.Outbound;
using Stockroom.Application.Reports;
using Stockroom.Domain.PutAway;
using Stockroom.Domain.Users;

namespace Stockroom.Application;

public static class DependencyInjection
{
    private static readonly IPutAwayStrategy[] KnownStrategies =
        [new ConsolidateSameLotStrategy(), new EmptyBinInAbcZoneStrategy(), new AnyBinWithCapacityStrategy()];

    /// <param name="putAwayStrategyOrder">Strategy names in priority order (config <c>PutAway:Strategies</c>); defaults to all three in the order above.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services, IReadOnlyList<string>? putAwayStrategyOrder = null)
    {
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IPutAwayStrategy>(BuildChain(putAwayStrategyOrder));

        services.AddScoped<AuthService>();
        services.AddScoped<MasterDataService>();
        services.AddScoped<PutAwayPlanner>();
        services.AddScoped<InboundService>();
        services.AddScoped<OutboundService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<ReportService>();
        return services;
    }

    private static PutAwayStrategyChain BuildChain(IReadOnlyList<string>? order)
    {
        if (order is null || order.Count == 0)
        {
            return new PutAwayStrategyChain(KnownStrategies);
        }

        return new PutAwayStrategyChain(order.Select(name =>
            KnownStrategies.SingleOrDefault(s => s.Name == name)
            ?? throw new InvalidOperationException(
                $"Unknown put-away strategy '{name}'. Known: {string.Join(", ", KnownStrategies.Select(s => s.Name))}.")));
    }
}
