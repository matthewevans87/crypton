using Crypton.Configuration;
using MonitoringDashboard.Hubs;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using Serilog;
using DashboardPriceTicker = MonitoringDashboard.Models.PriceTicker;

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
