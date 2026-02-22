using AgentRunner.Agents;
using AgentRunner.Api;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using AgentRunner.Tools;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var configLoader = new ConfigLoader("config.yaml");
var config = configLoader.Load();

var logger = new EventLogger(Path.Combine(config.Logging.OutputPath, "agent_runner.log"));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Starting Agent Runner...");

var artifactManager = new ArtifactManager(config.Storage);
var mailboxManager = new MailboxManager(config.Storage);
var toolRegistry = new ToolRegistry(config);
var stateMachine = new LoopStateMachine();
var statePersistence = new StatePersistence("state.json");
var contextBuilder = new AgentContextBuilder(artifactManager, mailboxManager, toolRegistry, config);
var agentInvoker = new AgentInvoker(config, toolRegistry.Executor);
var agentRunnerService = new AgentRunnerService(
    config,
    stateMachine,
    statePersistence,
    artifactManager,
    mailboxManager,
    contextBuilder,
    agentInvoker,
    logger);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(artifactManager);
builder.Services.AddSingleton(mailboxManager);
builder.Services.AddSingleton(toolRegistry);
builder.Services.AddSingleton(stateMachine);
builder.Services.AddSingleton(statePersistence);
builder.Services.AddSingleton(contextBuilder);
builder.Services.AddSingleton(agentInvoker);
builder.Services.AddSingleton(agentRunnerService);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

_ = Task.Run(async () =>
{
    try
    {
        await agentRunnerService.StartAsync();
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

Log.Information("Agent Runner configured on {Host}:{Port}", config.Api.Host, config.Api.Port);

app.Run($"http://{config.Api.Host}:{config.Api.Port}");
