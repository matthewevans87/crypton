using AgentRunner.Agents;
using AgentRunner.Api;
using AgentRunner.Artifacts;
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

// Load a .env file (if present) into environment variables before the host builder
// reads IConfiguration. Real environment variables (e.g. from Docker) are never
// overwritten, so the precedence is: env vars > .env file > appsettings.json.
DotEnvLoader.Load();

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
