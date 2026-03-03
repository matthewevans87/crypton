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
using DashboardReasoningStep = MonitoringDashboard.Models.ReasoningStep;
using DashboardToolCall = MonitoringDashboard.Models.ToolCall;

// Load .env file before the host builder so values flow into IConfiguration.
// Prefer ~/.config/crypton/.env (user-level secrets, never committed), then
// fall back to a .env found by walking up from cwd.
var userEnvFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".config", "crypton", ".env");
if (File.Exists(userEnvFile))
    DotEnvLoader.Load(new FileInfo(userEnvFile));
else
    DotEnvLoader.Load();

// Map short-form env var names to ASP.NET Core's double-underscore hierarchy.
var envAliases = new Dictionary<string, string>
{
    ["AGENT_RUNNER_API_KEY"] = "AgentRunner__ApiKey",
};
foreach (var (shortKey, fullKey) in envAliases)
{
    var val = Environment.GetEnvironmentVariable(shortKey);
    if (val is not null && Environment.GetEnvironmentVariable(fullKey) is null)
        Environment.SetEnvironmentVariable(fullKey, val);
}

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

// AgentRunner: wire SignalR events → forward to DashboardHub, then connect with retry.
agentRunnerClient.OnStatusUpdate += (sender, payload) =>
{
    try
    {
        string? stateName = null;
        if (payload.TryGetProperty("current_state", out var cs)) stateName = cs.GetString();
        else if (payload.TryGetProperty("state", out var s)) stateName = s.GetString();

        if (stateName is null) return;

        _ = dashboardHubContext.Clients.All.AgentStateChanged(new DashboardAgentState
        {
            CurrentState = stateName,
            IsRunning = stateName is not ("WaitingForNextCycle" or "Idle" or "Paused"),
            StateStartedAt = DateTime.UtcNow,
        });
    }
    catch (Exception ex) { logger.LogWarning(ex, "AgentRunner StatusUpdate mapping error"); }
};

agentRunnerClient.OnStepStarted += (sender, payload) =>
{
    try
    {
        var stepName = payload.TryGetProperty("step_name", out var sn) ? sn.GetString() : null;
        if (stepName is null) return;

        _ = dashboardHubContext.Clients.All.AgentStateChanged(new DashboardAgentState
        {
            CurrentState = stepName,
            ActiveAgent = stepName,
            IsRunning = true,
            StateStartedAt = DateTime.UtcNow,
        });
    }
    catch (Exception ex) { logger.LogWarning(ex, "AgentRunner StepStarted mapping error"); }
};

agentRunnerClient.OnTokenReceived += (sender, payload) =>
{
    try
    {
        var token = payload.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
        _ = dashboardHubContext.Clients.All.ReasoningUpdated(new DashboardReasoningStep
        {
            Timestamp = DateTime.UtcNow,
            Content = token,
            Token = token
        });
    }
    catch (Exception ex) { logger.LogWarning(ex, "AgentRunner TokenReceived mapping error"); }
};

agentRunnerClient.OnToolCallStarted += (sender, payload) =>
{
    try
    {
        _ = dashboardHubContext.Clients.All.ToolCallStarted(new DashboardToolCall
        {
            Id = payload.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            ToolName = payload.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "",
            Input = payload.TryGetProperty("input", out var inp) ? inp.GetString() ?? "" : "",
            CalledAt = payload.TryGetProperty("called_at", out var ca) ? ca.GetDateTime() : DateTime.UtcNow,
            IsCompleted = false
        });
    }
    catch (Exception ex) { logger.LogWarning(ex, "AgentRunner ToolCallStarted mapping error"); }
};

agentRunnerClient.OnToolCallCompleted += (sender, payload) =>
{
    try
    {
        var success = !payload.TryGetProperty("success", out var succ) || succ.GetBoolean();
        _ = dashboardHubContext.Clients.All.ToolCallCompleted(new DashboardToolCall
        {
            Id = payload.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            ToolName = payload.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "",
            Input = "",
            Output = payload.TryGetProperty("output", out var o) ? o.GetString() : null,
            CalledAt = payload.TryGetProperty("called_at", out var ca) ? ca.GetDateTime() : DateTime.UtcNow,
            DurationMs = payload.TryGetProperty("duration_ms", out var dur) ? dur.GetInt64() : 0,
            IsCompleted = true,
            IsError = !success,
            ErrorMessage = payload.TryGetProperty("error_message", out var em) ? em.GetString() : null,
        });
    }
    catch (Exception ex) { logger.LogWarning(ex, "AgentRunner ToolCallCompleted mapping error"); }
};

_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            if (!agentRunnerClient.IsConnected)
            {
                await agentRunnerClient.ConnectAsync();
                logger.LogInformation("Connected to AgentRunner SignalR hub");
            }
            break;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to AgentRunner, retrying in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
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
