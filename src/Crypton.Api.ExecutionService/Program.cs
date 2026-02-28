using Crypton.Configuration;
using Crypton.Api.ExecutionService.Cli;
using Crypton.Api.ExecutionService.Configuration;

// Load .env file before the host builder so values flow into IConfiguration.
DotEnvLoader.Load();

var cliVerbs = new[] { "status", "safe-mode", "strategy", "promote-to-live", "demote-to-paper" };

if (args.Length > 0 && cliVerbs.Contains(args[0]) && !args.Contains("--service"))
{
    // CLI mode
    return await CliRunner.RunAsync(args);
}

// Service mode
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExecutionServiceCore(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapControllers();
app.MapHub<Crypton.Api.ExecutionService.Hubs.ExecutionHub>("/hubs/execution");
app.MapHealthChecks("/health/live");

await app.RunAsync();
return 0;
