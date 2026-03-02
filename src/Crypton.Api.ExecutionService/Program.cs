using Crypton.Configuration;
using Crypton.Api.ExecutionService.Cli;
using Crypton.Api.ExecutionService.Configuration;
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

// ── Environment variable aliases ─────────────────────────────────────────────
// Allow short, conventional names (e.g. KRAKEN_API_KEY) to flow into the
// strongly-typed ExecutionServiceConfig without the full config-key prefix.
ApplyEnvAlias("KRAKEN_API_KEY", "EXECUTION_SERVICE__KRAKEN__ApiKey");
ApplyEnvAlias("KRAKEN_API_SECRET", "EXECUTION_SERVICE__KRAKEN__ApiSecret");
ApplyEnvAlias("KRAKEN_SECRET_KEY", "EXECUTION_SERVICE__KRAKEN__ApiSecret");
ApplyEnvAlias("MARKET_DATA_URL", "EXECUTION_SERVICE__MarketDataServiceUrl");

static void ApplyEnvAlias(string source, string target)
{
    var value = Environment.GetEnvironmentVariable(source);
    if (!string.IsNullOrEmpty(value) &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(target)))
    {
        Environment.SetEnvironmentVariable(target, value);
    }
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

builder.Services.AddExecutionServiceCore(builder.Configuration);
builder.Services.AddControllers();
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

