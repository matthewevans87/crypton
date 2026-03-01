using AgentRunner.Agents;
using AgentRunner.Api;
using AgentRunner.Artifacts;
using AgentRunner.Cli;
using AgentRunner.Configuration;
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

// Map short-form env var names (common in .env files) to the double-underscore
// hierarchy form that ASP.NET Core IConfiguration expects. Only applied when the
// target key is not already set (short form wins if both are present).
var envAliases = new Dictionary<string, string>
{
    ["BRAVE_SEARCH_API_KEY"] = "Tools__BraveSearch__ApiKey",
    ["AGENT_RUNNER_API_KEY"]  = "Api__ApiKey",
};
foreach (var (shortKey, fullKey) in envAliases)
{
    var val = Environment.GetEnvironmentVariable(shortKey);
    if (val is not null && Environment.GetEnvironmentVariable(fullKey) is null)
        Environment.SetEnvironmentVariable(fullKey, val);
}

var builder = WebApplication.CreateBuilder(args);

// Bind configuration from IConfiguration (appsettings.json + env vars + cmdline).
// Secrets are provided via environment variables using the __ hierarchy convention:
//   Tools__BraveSearch__ApiKey  →  AgentRunnerConfig.Tools.BraveSearch.ApiKey
//   Api__ApiKey                 →  AgentRunnerConfig.Api.ApiKey
var config = builder.Configuration.Get<AgentRunnerConfig>()
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

if (string.IsNullOrEmpty(config.Tools.BraveSearch.ApiKey))
    Log.Warning("Brave Search API key not configured. Set env var: Tools__BraveSearch__ApiKey");

if (string.IsNullOrEmpty(config.Api.ApiKey))
    Log.Warning("Agent Runner API key not configured. Set env var: Api__ApiKey");

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

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseRouting();
app.MapMetrics();

app.MapControllers();

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
