using Crypton.Api.ExecutionService.Api;
using Crypton.Api.ExecutionService.Exchange;
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

        // Market data source: connects to the MarketData SignalR hub and fans out snapshots.
        // Registered as both IMarketDataSource (for PaperTradingAdapter) and IHostedService
        // (so the hub connection is maintained for the lifetime of the process).
        services.AddSingleton<MarketDataServiceClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ExecutionServiceConfig>>().Value;
            var logger = sp.GetRequiredService<ILogger<MarketDataServiceClient>>();
            return new MarketDataServiceClient(cfg.MarketDataServiceUrl, logger);
        });
        services.AddSingleton<IMarketDataSource>(sp => sp.GetRequiredService<MarketDataServiceClient>());
        services.AddHostedService(sp => sp.GetRequiredService<MarketDataServiceClient>());

        services.AddSingleton<PaperTradingAdapter>();
        services.AddSingleton<DelegatingExchangeAdapter>();
        services.AddSingleton<IExchangeAdapter>(sp =>
            sp.GetRequiredService<DelegatingExchangeAdapter>());

        // ── Kraken live adapters ──────────────────────────────────────────────
        // Always registered so they are available the moment mode is switched to live.
        // DelegatingExchangeAdapter checks CurrentMode at call time and routes accordingly.

        services.AddSingleton<KrakenRestAdapter>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ExecutionServiceConfig>>().Value.Kraken;
            var http = new HttpClient { BaseAddress = new Uri(cfg.RestBaseUrl) };
            var logger = sp.GetRequiredService<ILogger<KrakenRestAdapter>>();
            return new KrakenRestAdapter(cfg.ApiKey, cfg.ApiSecret, http, logger);
        });

        services.AddSingleton<KrakenWebSocketAdapter>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<ExecutionServiceConfig>>().Value.Kraken;
            var logger = sp.GetRequiredService<ILogger<KrakenWebSocketAdapter>>();
            return new KrakenWebSocketAdapter(
                cfg.WsBaseUrl,
                cfg.MaxReconnectAttempts,
                cfg.ReconnectDelaySeconds,
                logger);
        });

        // Wire live adapters into DelegatingExchangeAdapter on first start.
        services.AddHostedService(sp => new LiveAdapterWiring(
            sp.GetRequiredService<DelegatingExchangeAdapter>(),
            sp.GetRequiredService<KrakenWebSocketAdapter>(),
            sp.GetRequiredService<KrakenRestAdapter>()));

        // Authenticated Kraken WS for execution fills (only active in live mode).
        services.AddHostedService(sp => new KrakenWsExecutionAdapter(
            sp.GetRequiredService<KrakenRestAdapter>(),
            sp.GetRequiredService<OrderRouter>(),
            sp.GetRequiredService<IOperationModeService>(),
            sp.GetRequiredService<ILogger<KrakenWsExecutionAdapter>>()));

        // ── Strategy ──────────────────────────────────────────────────────────
        services.AddSingleton<StrategyValidator>();
        services.AddSingleton<ConditionParser>();
        services.AddSingleton<StrategyService>();
        services.AddSingleton<IStrategyService>(sp => sp.GetRequiredService<StrategyService>());
        services.AddHostedService(sp => sp.GetRequiredService<StrategyService>());

        // Portfolio risk enforcement
        services.AddSingleton<PortfolioRiskEnforcer>();

        // PositionRegistry — file paths resolved from config.
        services.AddSingleton(sp =>
        {
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

        services.AddSingleton<PositionSizingCalculator>();
        services.AddSingleton<OrderRouter>();

        // ── Resilience ────────────────────────────────────────────────────────
        services.AddSingleton<FailureTracker>();
        services.AddSingleton<SafeModeController>();
        services.AddSingleton<ISafeModeController>(sp => sp.GetRequiredService<SafeModeController>());
        services.AddSingleton<ResilienceWiring>();
        services.AddHostedService(sp => sp.GetRequiredService<ResilienceWiring>());

        // Reconciliation — runs once on startup.
        services.AddSingleton<ReconciliationService>();
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliationService>());

        // ── Execution engine ──────────────────────────────────────────────────
        services.AddSingleton<MarketDataHub>();
        services.AddHostedService(sp => sp.GetRequiredService<MarketDataHub>());
        services.AddSingleton<EntryEvaluator>();
        services.AddSingleton<ExitEvaluator>();
        services.AddSingleton<ExecutionEngine>();
        services.AddHostedService(sp => sp.GetRequiredService<ExecutionEngine>());

        // ── API ───────────────────────────────────────────────────────────────
        services.AddScoped<ApiKeyAuthFilter>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<IMetricsCollector>(sp => sp.GetRequiredService<MetricsCollector>());

        // SignalR broadcaster
        services.AddSingleton<ExecutionHubBroadcaster>();
        services.AddHostedService(sp => sp.GetRequiredService<ExecutionHubBroadcaster>());

        return services;
    }
}

/// <summary>
/// One-shot hosted service that calls <see cref="DelegatingExchangeAdapter.SetLiveAdapters"/>
/// at startup so the Kraken live adapters are available when the mode is later switched to live.
/// </summary>
internal sealed class LiveAdapterWiring(
    DelegatingExchangeAdapter delegating,
    KrakenWebSocketAdapter ws,
    KrakenRestAdapter rest) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        delegating.SetLiveAdapters(ws, rest);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
