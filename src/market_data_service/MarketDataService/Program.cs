using MarketDataService.Adapters;
using MarketDataService.Hubs;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.SignalR;
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
    builder.Services.AddSignalR();

    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<IMarketDataCache, InMemoryMarketDataCache>();
    builder.Services.AddSingleton<IExchangeAdapter, KrakenExchangeAdapter>();
    builder.Services.AddSingleton<ITechnicalIndicatorService, TechnicalIndicatorService>();

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

    exchangeAdapter.OnPriceUpdate += (sender, ticker) =>
    {
        cache.SetPrice(ticker);
        _ = hubContext.Clients.All.OnPriceUpdate(ticker);
    };

    exchangeAdapter.OnOrderBookUpdate += (sender, orderBook) =>
    {
        cache.SetOrderBook(orderBook);
        _ = hubContext.Clients.All.OnOrderBookUpdate(orderBook);
    };

    exchangeAdapter.OnConnectionStateChanged += (sender, isConnected) =>
    {
        logger.LogInformation("Exchange connection state changed: {IsConnected}", isConnected);
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

    app.MapControllers();
    app.MapHub<MarketDataHub>("/hubs/marketdata");

    app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
    app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", exchange = exchangeAdapter.ExchangeName, connected = exchangeAdapter.IsConnected }));

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
