using AgentRunner.Agents;
using AgentRunner.Api;
using AgentRunner.Artifacts;
using AgentRunner.Cli;
using AgentRunner.Configuration;
using AgentRunner.Hubs;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.Telemetry;
using AgentRunner.StateMachine;
using AgentRunner.Tools;
using Crypton.Configuration;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;

// Load a .env file into environment variables before the host builder reads
// IConfiguration. Precedence: real env vars > .env file > appsettings.json.
//
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
        DotEnvLoader.Load();  // walk up from cwd
}

var builder = WebApplication.CreateBuilder(args);

var effectiveConfiguration = BuildServiceScopedConfiguration(builder.Configuration, "AgentRunner");

// Bind configuration from IConfiguration (appsettings.json + env vars + cmdline).
// Secrets are provided via environment variables using the __ hierarchy convention:
//   AGENTRUNNER__TOOLS__BRAVESEARCH__APIKEY  →  AgentRunnerConfig.Tools.BraveSearch.ApiKey
//   AGENTRUNNER__API__APIKEY                 →  AgentRunnerConfig.Api.ApiKey
var config = effectiveConfiguration.Get<AgentRunnerConfig>()
    ?? throw new InvalidOperationException("Failed to bind AgentRunnerConfig from IConfiguration.");

var logger = new EventLogger(
    Path.Combine(config.Logging.OutputPath, "agent_runner.log"),
    config.Logging.MaxFileSizeMb,
    config.Logging.MaxFileCount);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Starting Agent Runner...");

ValidateAgentRunnerConfiguration(config);

var artifactManager = new ArtifactManager(config.Storage);
var mailboxManager = new MailboxManager(config.Storage);
var toolRegistry = new ToolRegistry(config);
var stateMachine = new LoopStateMachine();
var statePersistence = new StatePersistence("state.json");
var contextBuilder = new AgentContextBuilder(artifactManager, mailboxManager, toolRegistry, config);
var agentInvoker = new AgentInvoker(config, toolRegistry.Executor);
var metricsCollector = new MetricsCollector();
var agentRunnerService = new AgentRunnerService(
    config,
    stateMachine,
    statePersistence,
    artifactManager,
    mailboxManager,
    contextBuilder,
    agentInvoker,
    logger,
    metricsCollector);

// CLI mode: `dotnet run -- --cli <command> [options]`
// Run a single step or full cycle with verbose console output, then exit.
if (args.Contains("--cli"))
{
    await CliRunner.RunAsync(args, config, artifactManager, contextBuilder, agentInvoker);
    return;
}

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(artifactManager);
builder.Services.AddSingleton(mailboxManager);
builder.Services.AddSingleton(toolRegistry);
builder.Services.AddSingleton(stateMachine);
builder.Services.AddSingleton(statePersistence);
builder.Services.AddSingleton(contextBuilder);
builder.Services.AddSingleton(agentInvoker);
builder.Services.AddSingleton(agentRunnerService);
builder.Services.AddSingleton(metricsCollector);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHostedService<AgentRunnerHubBroadcaster>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseRouting();
app.MapMetrics();

app.MapControllers();
app.MapHub<AgentRunnerHub>("/hubs/agent-runner");

_ = Task.Run(async () =>
{
    try
    {
        Log.Information("Starting agent runner service...");
        await agentRunnerService.StartAsync();
        Log.Information("Agent runner service started successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to start Agent Runner");
    }
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async () =>
{
    try
    {
        await agentRunnerService.StopAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during shutdown");
    }
});

var listenUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? $"http://{config.Api.Host}:{config.Api.Port}";

Log.Information("Agent Runner listening on {Url}", listenUrl);

app.Run(listenUrl);

static void ValidateAgentRunnerConfiguration(AgentRunnerConfig config)
{
    var errors = new List<string>();

    if (string.IsNullOrWhiteSpace(config.Tools.BraveSearch.ApiKey))
    {
        errors.Add("Missing required configuration 'tools:braveSearch:apiKey' (env: AGENTRUNNER__TOOLS__BRAVESEARCH__APIKEY).");
    }

    if (string.IsNullOrWhiteSpace(config.Api.ApiKey))
    {
        errors.Add("Missing required configuration 'api:apiKey' (env: AGENTRUNNER__API__APIKEY).");
    }

    if (!Uri.TryCreate(config.Tools.ExecutionService.BaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'tools:executionService:baseUrl' must be an absolute URI.");
    }

    if (!Uri.TryCreate(config.Tools.MarketDataService.BaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'tools:marketDataService:baseUrl' must be an absolute URI.");
    }

    if (errors.Count == 0)
    {
        return;
    }

    foreach (var error in errors)
    {
        Log.Error("Configuration contract violation: {ConfigError}", error);
    }

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
