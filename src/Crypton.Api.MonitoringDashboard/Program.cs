using Crypton.Configuration;
using MonitoringDashboard.Hubs;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using Serilog;
using System.Text.Json;
using DashboardPriceTicker = MonitoringDashboard.Models.PriceTicker;
using DashboardOrderBook = MonitoringDashboard.Models.OrderBook;
using DashboardOrderBookEntry = MonitoringDashboard.Models.OrderBookEntry;
using DashboardMarketTrade = MonitoringDashboard.Models.MarketTrade;
using DashboardPosition = MonitoringDashboard.Models.Position;
using DashboardAgentState = MonitoringDashboard.Models.AgentState;

// Load .env file before the host builder so values flow into IConfiguration.
DotEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();

var marketDataServiceUrl = builder.Configuration["MarketDataService:Url"] ?? "http://localhost:5002";
builder.Services.AddSingleton<IMarketDataServiceClient>(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var logger = sp.GetRequiredService<ILogger<MarketDataServiceClient>>();
    return new MarketDataServiceClient(marketDataServiceUrl, httpClient, logger);
});

var executionServiceUrl = builder.Configuration["ExecutionService:Url"] ?? "http://localhost:5004";
builder.Services.AddSingleton<IExecutionServiceClient>(sp =>
{
    var httpClient = new HttpClient();
    var logger = sp.GetRequiredService<ILogger<ExecutionServiceClient>>();
    return new ExecutionServiceClient(executionServiceUrl, httpClient, logger);
});

var agentRunnerUrl = builder.Configuration["AgentRunner:Url"] ?? "http://localhost:5003";
builder.Services.AddSingleton<IAgentRunnerClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<AgentRunnerClient>>();
    return new AgentRunnerClient(factory, agentRunnerUrl, logger);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

var marketDataClient = app.Services.GetRequiredService<IMarketDataServiceClient>();
var executionServiceClient = app.Services.GetRequiredService<IExecutionServiceClient>();
var agentRunnerClient = app.Services.GetRequiredService<IAgentRunnerClient>();
var dashboardHubContext = app.Services.GetRequiredService<IHubContext<DashboardHub, IDashboardClient>>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

marketDataClient.OnPriceUpdate += (sender, ticker) =>
{
    var dashboardTicker = new DashboardPriceTicker
    {
        Asset = ticker.Asset,
        Price = ticker.Price,
        Change24h = ticker.Change24h,
        ChangePercent24h = ticker.ChangePercent24h,
        Bid = ticker.Bid,
        Ask = ticker.Ask,
        High24h = ticker.High24h,
        Low24h = ticker.Low24h,
        Volume24h = ticker.Volume24h,
        LastUpdated = ticker.LastUpdated
    };
    _ = dashboardHubContext.Clients.All.PriceUpdated(dashboardTicker);
};

marketDataClient.OnConnectionStatus += (sender, isConnected) =>
{
    logger.LogInformation("Market Data Service connection status changed: {IsConnected}", isConnected);
};

marketDataClient.OnOrderBookUpdate += (sender, orderBook) =>
{
    var dashboardOrderBook = new DashboardOrderBook
    {
        Symbol = orderBook.Symbol,
        Bids = orderBook.Bids.Select(e => new DashboardOrderBookEntry { Price = e.Price, Quantity = e.Quantity, Count = e.Count }).ToList(),
        Asks = orderBook.Asks.Select(e => new DashboardOrderBookEntry { Price = e.Price, Quantity = e.Quantity, Count = e.Count }).ToList(),
        LastUpdated = orderBook.LastUpdated
    };
    _ = dashboardHubContext.Clients.All.OrderBookUpdated(dashboardOrderBook);
};

marketDataClient.OnTrade += (sender, trade) =>
{
    var dashboardTrade = new DashboardMarketTrade
    {
        Id = trade.Id,
        Symbol = trade.Symbol,
        Price = trade.Price,
        Quantity = trade.Quantity,
        Side = trade.Side,
        Timestamp = trade.Timestamp
    };
    _ = dashboardHubContext.Clients.All.TradeOccurred(dashboardTrade);
};

// ExecutionService: forward position changes to DashboardHub.PositionUpdated
executionServiceClient.OnPositionUpdate += (sender, posJson) =>
{
    try
    {
        var pos = new DashboardPosition
        {
            Id = posJson.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Asset = posJson.TryGetProperty("asset", out var asset) ? asset.GetString() ?? "" : "",
            Direction = posJson.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "" : "",
            EntryPrice = posJson.TryGetProperty("average_entry_price", out var ep) ? ep.GetDecimal() : 0m,
            Size = posJson.TryGetProperty("quantity", out var qty) ? qty.GetDecimal() : 0m,
            OpenedAt = posJson.TryGetProperty("opened_at", out var oa) && oa.ValueKind != JsonValueKind.Null
                ? oa.GetDateTimeOffset().UtcDateTime : DateTime.UtcNow,
        };
        _ = dashboardHubContext.Clients.All.PositionUpdated(pos);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to map ExecutionService PositionUpdate to DashboardHub");
    }
};

_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            if (!marketDataClient.IsConnected)
            {
                await marketDataClient.ConnectAsync();
                logger.LogInformation("Connected to Market Data Service");
            }
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Market Data Service, retrying in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
});

// Connect to ExecutionService SignalR hub with automatic retry.
_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            if (!executionServiceClient.IsConnected)
            {
                await executionServiceClient.ConnectAsync();
                logger.LogInformation("Connected to ExecutionService");
            }
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to ExecutionService, retrying in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
});

// Poll AgentRunner every 5 s; push AgentStateChanged when the loop state transitions.
_ = Task.Run(async () =>
{
    string? lastState = null;
    using var cts = new CancellationTokenSource();
    while (true)
    {
        try
        {
            var status = await agentRunnerClient.GetStatusAsync(cts.Token);
            if (status is not null)
            {
                var currentState = status.Value.TryGetProperty("currentState", out var cs)
                    ? cs.GetString() : null;
                if (currentState != null && currentState != lastState)
                {
                    lastState = currentState;
                    var agentState = new DashboardAgentState
                    {
                        CurrentState = currentState,
                        IsRunning = currentState is not ("WaitingForNextCycle" or "Idle"),
                        StateStartedAt = DateTime.UtcNow,
                    };
                    _ = dashboardHubContext.Clients.All.AgentStateChanged(agentState);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AgentRunner polling error");
        }
        await Task.Delay(TimeSpan.FromSeconds(5));
    }
});

app.UseSerilogRequestLogging();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.Run();
