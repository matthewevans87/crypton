using Crypton.Configuration;
using Crypton.Api.ExecutionService.Cli;
using Crypton.Api.ExecutionService.Configuration;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

// ── Environment + secrets loading ────────────────────────────────────────────
// Priority order:
//   1. Explicit --env-file <path> argument (~ is expanded to the user's home dir).
//   2. Well-known per-user config location: ~/.config/crypton/.env
//   3. Auto-walk from the current working directory (DotEnvLoader.Load() default).
//
// This ensures credentials are never baked into source or Docker images.

var envFileIndex = Array.IndexOf(args, "--env-file");
if (envFileIndex >= 0 && envFileIndex + 1 < args.Length)
{
    var rawPath = args[envFileIndex + 1];
    var resolved = rawPath.StartsWith("~/")
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            rawPath[2..])
        : rawPath;
    DotEnvLoader.Load(new FileInfo(resolved));
}
else
{
    var userEnvPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "crypton", ".env");

    if (File.Exists(userEnvPath))
        DotEnvLoader.Load(new FileInfo(userEnvPath));
    else
        DotEnvLoader.Load();
}

// ── CLI mode detection ────────────────────────────────────────────────────────
// Defined verbs that route to CliRunner rather than the ASP.NET host.
// Two ways to enter CLI mode:
//   1. First argument is a known verb: `dotnet run -- status`
//   2. Explicit --cli flag: `dotnet run -- --cli status`
// Pass --service to force service mode even when a verb is present.
var allCliVerbs = new[]
{
    "status", "safe-mode", "set-mode", "run-order", "reconcile",
    "promote-to-live", "demote-to-paper"
};

var isCliMode = !args.Contains("--service") &&
    (args.Contains("--cli") ||
     (args.Length > 0 && allCliVerbs.Contains(args[0])));

if (isCliMode)
{
    return await CliRunner.RunAsync(args);
}

// ── Service mode ──────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

ValidateExecutionServiceConfiguration(builder.Configuration);

builder.Services.AddExecutionServiceCore(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();
app.MapHub<Crypton.Api.ExecutionService.Hubs.ExecutionHub>("/hubs/execution");
app.MapHealthChecks("/health/live");

await app.RunAsync();
return 0;

static void ValidateExecutionServiceConfiguration(IConfiguration configuration)
{
    using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
    var startupLogger = startupLoggerFactory.CreateLogger("ExecutionService.Startup");

    var errors = new List<string>();

    var config = configuration.GetSection("executionService").Get<ExecutionServiceConfig>()
        ?? new ExecutionServiceConfig();

    if (string.IsNullOrWhiteSpace(config.Api.ApiKey))
    {
        errors.Add("Missing required configuration 'executionService:api:apiKey' (env: EXECUTIONSERVICE__API__APIKEY).");
    }

    if (string.IsNullOrWhiteSpace(config.Kraken.ApiKey))
    {
        errors.Add("Missing required configuration 'executionService:kraken:apiKey' (env: EXECUTIONSERVICE__KRAKEN__APIKEY).");
    }

    if (string.IsNullOrWhiteSpace(config.Kraken.ApiSecret))
    {
        errors.Add("Missing required configuration 'executionService:kraken:apiSecret' (env: EXECUTIONSERVICE__KRAKEN__APISECRET).");
    }

    if (!Uri.TryCreate(config.Kraken.RestBaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'executionService:kraken:restBaseUrl' must be an absolute URI.");
    }

    if (!Uri.TryCreate(config.Kraken.WsBaseUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'executionService:kraken:wsBaseUrl' must be an absolute URI.");
    }

    if (!Uri.TryCreate(config.MarketDataServiceUrl, UriKind.Absolute, out _))
    {
        errors.Add("Configuration 'executionService:marketDataServiceUrl' must be an absolute URI.");
    }

    if (errors.Count == 0)
    {
        return;
    }

    foreach (var error in errors)
    {
        startupLogger.LogError("Configuration contract violation: {ConfigError}", error);
    }

    throw new InvalidOperationException(
        $"Execution Service configuration validation failed with {errors.Count} error(s). See ERROR logs.");
}

