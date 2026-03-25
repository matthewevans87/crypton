using Crypton.Configuration;
using MarketDataService.Adapters;
using MarketDataService.Configuration;
using MarketDataService.Hubs;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using Serilog;

// Load .env file before the host builder so values flow into IConfiguration.
LoadEnvironment(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting Market Data Service");

    var builder = WebApplication.CreateBuilder(args);

    var effectiveConfiguration = BuildServiceScopedConfiguration(builder.Configuration, "MarketData");

    var marketDataConfig = effectiveConfiguration.Get<MarketDataConfig>()
        ?? throw new InvalidOperationException("Failed to bind MarketDataConfig from IConfiguration.");

    var startupConfigErrors = ValidateMarketDataConfiguration(marketDataConfig);
    if (startupConfigErrors.Count > 0)
    {
        foreach (var startupConfigError in startupConfigErrors)
        {
            Log.Error("Configuration contract violation: {ConfigError}", startupConfigError);
        }

        throw new InvalidOperationException(
            $"Market Data Service configuration validation failed with {startupConfigErrors.Count} error(s). See ERROR logs.");
    }

    builder.Host.UseSerilog();

    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();
    builder.Services.AddSignalR();

    builder.Services.AddSingleton(marketDataConfig);
    builder.Services.AddHttpClient();
    builder.Services.AddLogging();
    builder.Services.AddSingleton<IMarketDataCache, InMemoryMarketDataCache>();
    var useMock = marketDataConfig.Exchange.UseMock;

    if (useMock)
    {
        builder.Services.AddSingleton<IExchangeAdapter>(sp =>
            new MockExchangeAdapter(sp.GetRequiredService<ILogger<MockExchangeAdapter>>()));
    }
    else
    {
        builder.Services.AddSingleton<IExchangeAdapter>(sp =>
            new KrakenExchangeAdapter(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILogger<KrakenExchangeAdapter>>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<MarketDataConfig>().Kraken));
    }
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

        return Results.Ok(new
        {
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

static void LoadEnvironment(string[] args)
{
    var envFileIndex = Array.IndexOf(args, "--env-file");
    if (envFileIndex >= 0 && envFileIndex + 1 < args.Length)
    {
        var rawPath = args[envFileIndex + 1];
        var resolved = rawPath.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                rawPath[2..])
            : rawPath;

        DotEnvLoader.Load(new FileInfo(resolved));
        return;
    }

    var userEnvPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "crypton", ".env");

    if (File.Exists(userEnvPath))
    {
        DotEnvLoader.Load(new FileInfo(userEnvPath));
        return;
    }

    DotEnvLoader.Load();
}

static List<string> ValidateMarketDataConfiguration(MarketDataConfig config)
{
    var errors = new List<string>();

    if (!config.Exchange.UseMock)
    {
        if (string.IsNullOrWhiteSpace(config.Kraken.ApiKey))
        {
            errors.Add("Missing required configuration 'kraken:apiKey' (env: MARKETDATA__KRAKEN__APIKEY) when exchange:useMock=false.");
        }

        if (string.IsNullOrWhiteSpace(config.Kraken.ApiSecret))
        {
            errors.Add("Missing required configuration 'kraken:apiSecret' (env: MARKETDATA__KRAKEN__APISECRET) when exchange:useMock=false.");
        }
    }

    if (!Uri.TryCreate(config.Kraken.RestBaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'kraken:restBaseUrl' must be an absolute URI.");
    }

    if (!Uri.TryCreate(config.Kraken.WsBaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'kraken:wsBaseUrl' must be an absolute URI.");
    }

    return errors;
}

static IConfiguration BuildServiceScopedConfiguration(IConfiguration configuration, string serviceName)
{
    var serviceOverrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var serviceSection = configuration.GetSection(serviceName);

    void Flatten(IConfigurationSection section, string pathPrefix)
    {
        foreach (var child in section.GetChildren())
        {
            var childPath = string.IsNullOrEmpty(pathPrefix)
                ? child.Key
                : $"{pathPrefix}:{child.Key}";

            if (child.Value is not null)
            {
                serviceOverrides[childPath] = child.Value;
            }

            Flatten(child, childPath);
        }
    }

    Flatten(serviceSection, string.Empty);

    return new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(serviceOverrides)
        .Build();
}
