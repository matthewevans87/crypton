using Crypton.Api.ExecutionService.Api;
using Crypton.Api.ExecutionService.Execution;
using Crypton.Api.ExecutionService.Hubs;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Metrics;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExecutionServiceCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ExecutionServiceConfig>(
            configuration.GetSection("execution_service"));

        // Event logging
        services.AddSingleton<IEventLogger, FileEventLogger>();

        // Operation mode
        services.AddSingleton<OperationModeService>();
        services.AddSingleton<IOperationModeService>(sp => sp.GetRequiredService<OperationModeService>());
        services.AddSingleton<IMarketDataSource, NullMarketDataSource>();
        services.AddSingleton<PaperTradingAdapter>();
        services.AddSingleton<DelegatingExchangeAdapter>();
        services.AddSingleton<Exchange.IExchangeAdapter>(sp =>
            sp.GetRequiredService<DelegatingExchangeAdapter>());

        // Strategy validation
        services.AddSingleton<StrategyValidator>();

        // Condition parser
        services.AddSingleton<ConditionParser>();

        // Strategy service (hot-reload + validity window monitor)
        services.AddSingleton<StrategyService>();
        services.AddSingleton<IStrategyService>(sp => sp.GetRequiredService<StrategyService>());
        services.AddHostedService(sp => sp.GetRequiredService<StrategyService>());

        // Portfolio risk enforcement
        services.AddSingleton<PortfolioRiskEnforcer>();

        // PositionRegistry requires file paths from config — register as factory.
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ExecutionServiceConfig>>().Value;
            var eventLogger = sp.GetRequiredService<IEventLogger>();
            var logger = sp.GetRequiredService<ILogger<PositionRegistry>>();
            var registry = new PositionRegistry(
                "artifacts/positions.json",
                "artifacts/trades.json",
                eventLogger,
                logger);
            registry.Load();
            return registry;
        });

        // NOTE: PositionSizingCalculator and OrderRouter depend on IExchangeAdapter.
        // IExchangeAdapter registration happens in the operation mode setup (paper/live).
        // Register these after IExchangeAdapter is available in that setup.
        services.AddSingleton<PositionSizingCalculator>();
        services.AddSingleton<OrderRouter>();

        // Resilience: FailureTracker and SafeModeController.
        // FailureTracker is registered first (no deps on OrderRouter/SafeModeController).
        services.AddSingleton<FailureTracker>();
        services.AddSingleton<SafeModeController>();
        services.AddSingleton<ISafeModeController>(sp => sp.GetRequiredService<SafeModeController>());

        // Wire FailureTracker.OnSafeModeTriggered -> SafeModeController.ActivateAsync after DI resolves.
        services.AddSingleton<ResilienceWiring>();
        services.AddHostedService(sp => sp.GetRequiredService<ResilienceWiring>());

        // Reconciliation runs once on startup.
        services.AddSingleton<ReconciliationService>();
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliationService>());

        // Condition Evaluation Engine — depends on IExchangeAdapter, so registered alongside
        // OrderRouter/PositionSizingCalculator. Hosted services are started after all adapters
        // are wired.
        services.AddSingleton<MarketDataHub>();
        services.AddHostedService(sp => sp.GetRequiredService<MarketDataHub>());
        services.AddSingleton<EntryEvaluator>();
        services.AddSingleton<ExitEvaluator>();
        services.AddSingleton<ExecutionEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<ExecutionEngine>());

        // API
        services.AddScoped<ApiKeyAuthFilter>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<MetricsCollector>());

        // SignalR broadcaster
        services.AddSingleton<ExecutionHubBroadcaster>();
        services.AddHostedService(sp => sp.GetRequiredService<ExecutionHubBroadcaster>());

        return services;
    }
}
