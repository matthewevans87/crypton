using MarketDataService.Adapters;
using MarketDataService.Hubs;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting Market Data Service");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();
    builder.Services.AddSignalR();

    builder.Services.AddHttpClient();
    builder.Services.AddLogging();
    builder.Services.AddSingleton<IMarketDataCache, InMemoryMarketDataCache>();
    builder.Services.AddSingleton<IExchangeAdapter>(sp => 
        new KrakenExchangeAdapter(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ILogger<KrakenExchangeAdapter>>(),
            sp.GetRequiredService<ILoggerFactory>()));
    builder.Services.AddSingleton<ITechnicalIndicatorService, TechnicalIndicatorService>();
    builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    var exchangeAdapter = app.Services.GetRequiredService<IExchangeAdapter>();
    var cache = app.Services.GetRequiredService<IMarketDataCache>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var hubContext = app.Services.GetRequiredService<IHubContext<MarketDataHub, IMarketDataClient>>();
    var metrics = app.Services.GetRequiredService<IMetricsCollector>();

    exchangeAdapter.OnPriceUpdate += (sender, ticker) =>
    {
        cache.SetPrice(ticker);
        metrics.RecordPriceUpdateLatency(TimeSpan.Zero);
        
        var symbol = ticker.Asset;
        
        _ = hubContext.Clients.All.OnPriceUpdate(ticker);
        _ = hubContext.Clients.Group($"symbol_{symbol}").OnPriceUpdate(ticker);
        _ = hubContext.Clients.Group("prices").OnPriceUpdate(ticker);
    };

    exchangeAdapter.OnOrderBookUpdate += (sender, orderBook) =>
    {
        cache.SetOrderBook(orderBook);
        
        _ = hubContext.Clients.All.OnOrderBookUpdate(orderBook);
        _ = hubContext.Clients.Group($"symbol_{orderBook.Symbol}").OnOrderBookUpdate(orderBook);
    };

    exchangeAdapter.OnTrade += (sender, trade) =>
    {
        var symbol = trade.Symbol;
        
        _ = hubContext.Clients.All.OnTrade(trade);
        _ = hubContext.Clients.Group($"trades_{symbol}").OnTrade(trade);
    };

    exchangeAdapter.OnBalanceUpdate += (sender, balances) =>
    {
        _ = hubContext.Clients.All.OnBalanceUpdate(balances);
        _ = hubContext.Clients.Group("balance").OnBalanceUpdate(balances);
    };

    exchangeAdapter.OnConnectionStateChanged += (sender, isConnected) =>
    {
        logger.LogInformation("Exchange connection state changed: {IsConnected}", isConnected);
        metrics.RecordConnectionStateChanged(isConnected);
        metrics.RecordWsConnected(isConnected);
        _ = hubContext.Clients.All.OnConnectionStatus(isConnected);
    };

    _ = Task.Run(async () =>
    {
        try
        {
            await exchangeAdapter.ConnectAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to exchange");
        }
    });

    app.UseCors();

    app.MapOpenApi();
    app.MapScalarApiReference();

    app.MapControllers();
    app.MapHub<MarketDataHub>("/hubs/marketdata");

    app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
    app.MapGet("/health/ready", () => 
    {
        var metricsData = metrics.GetMetrics();
        var isHealthy = metricsData.IsHealthy;
        var status = isHealthy ? "ready" : "degraded";
        
        return Results.Ok(new { 
            status, 
            exchange = exchangeAdapter.ExchangeName, 
            connected = exchangeAdapter.IsConnected,
            pricesStale = metrics.IsPricesStale(),
            circuitBreakerState = exchangeAdapter.CircuitBreakerState.ToString()
        });
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Market Data Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
