using AgentRunner.Configuration;
using AgentRunner.Extensions;
using AgentRunner.Hubs;
using AgentRunner.Abstractions;
using Crypton.Configuration;
using Microsoft.Extensions.Configuration;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;

// ─── .env loading ─────────────────────────────────────────────────────────────
// Load environment variables from a .env file before IConfiguration is read.
// Precedence: real env vars > .env file > appsettings.json.
// Resolution order:
//   1. --env-file <path> CLI argument (supports ~ expansion)
//   2. ~/.config/crypton/.env  (user-level secrets, never committed)
//   3. Walk up from cwd looking for a .env file (project-level)
var envFileArg = Array.FindIndex(args, a => a == "--env-file");
if (envFileArg >= 0 && envFileArg + 1 < args.Length)
{
    var raw = args[envFileArg + 1];
    var resolved = raw.StartsWith("~/")
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), raw[2..])
        : raw;
    DotEnvLoader.Load(new FileInfo(resolved));
}
else
{
    var userEnv = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "crypton", ".env");
    if (File.Exists(userEnv))
        DotEnvLoader.Load(new FileInfo(userEnv));
    else
        DotEnvLoader.Load();
}

// ─── Configuration ────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

var effectiveConfiguration = BuildServiceScopedConfiguration(builder.Configuration, "AgentRunner");

var config = effectiveConfiguration.Get<AgentRunnerConfig>()
    ?? throw new InvalidOperationException("Failed to bind AgentRunnerConfig from IConfiguration.");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

Log.Information("Starting Agent Runner...");

ValidateAgentRunnerConfiguration(config);

// ─── Services ─────────────────────────────────────────────────────────────────

builder.Services
    .AddAgentRunnerCore(config)
    .AddAgentTools(config)
    .AddLlmExecution(config);

// The hub broadcaster is both the IAgentEventSink and a hosted background service
// (status polling loop). Register once as IAgentEventSink, resolve  for IHostedService.
builder.Services.AddSingleton<AgentRunnerHubBroadcaster>();
builder.Services.AddSingleton<IAgentEventSink>(sp => sp.GetRequiredService<AgentRunnerHubBroadcaster>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentRunnerHubBroadcaster>());

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// ─── Pipeline ─────────────────────────────────────────────────────────────────

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseRouting();
app.MapMetrics();

app.MapControllers();
app.MapHub<AgentRunnerHub>("/hubs/agent-runner");

// ─── Orchestrator startup ─────────────────────────────────────────────────────
// Start the learning-loop after the web server is fully up and accepting requests.
// The orchestrator handles its own restart-back-off logic; we just fire and forget.
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    var orchestrator = app.Services.GetRequiredService<ICycleOrchestrator>();
    _ = Task.Run(async () =>
    {
        try
        {
            Log.Information("Starting Agent Runner learning loop...");
            await orchestrator.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Agent Runner learning loop");
        }
    });
});

lifetime.ApplicationStopping.Register(() =>
{
    var orchestrator = app.Services.GetRequiredService<ICycleOrchestrator>();
    try
    {
        orchestrator.StopAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error stopping Agent Runner during shutdown");
    }
});

var listenUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? $"http://{config.Api.Host}:{config.Api.Port}";

Log.Information("Agent Runner listening on {Url}", listenUrl);

app.Run(listenUrl);

// ─── Helpers ─────────────────────────────────────────────────────────────────

static void ValidateAgentRunnerConfiguration(AgentRunnerConfig config)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(config.Tools.BraveSearch.ApiKey))
        errors.Add("Missing required config 'tools:braveSearch:apiKey' (env: AGENTRUNNER__TOOLS__BRAVESEARCH__APIKEY).");

    if (string.IsNullOrWhiteSpace(config.Api.ApiKey))
        errors.Add("Missing required config 'api:apiKey' (env: AGENTRUNNER__API__APIKEY).");

    if (!Uri.TryCreate(config.Tools.ExecutionService.BaseUrl, UriKind.Absolute, out _))
        errors.Add("Config 'tools:executionService:baseUrl' must be an absolute URI.");

    if (!Uri.TryCreate(config.Tools.MarketDataService.BaseUrl, UriKind.Absolute, out _))
        errors.Add("Config 'tools:marketDataService:baseUrl' must be an absolute URI.");

    if (errors.Count == 0)
        return;

    foreach (var error in errors)
        Log.Error("Configuration error: {ConfigError}", error);

    throw new InvalidOperationException(
        $"Agent Runner configuration validation failed with {errors.Count} error(s). See ERROR logs.");
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
                serviceOverrides[childPath] = child.Value;

            Flatten(child, childPath);
        }
    }

    Flatten(serviceSection, string.Empty);

    return new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(serviceOverrides)
        .Build();
}
