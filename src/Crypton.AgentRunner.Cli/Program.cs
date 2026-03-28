using AgentRunner.Abstractions;
using AgentRunner.Cli;
using AgentRunner.Configuration;
using AgentRunner.Extensions;
using Crypton.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── .env loading ────────────────────────────────────────────────────────────

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

// ─── Host ─────────────────────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration.GetSection("AgentRunner").Get<AgentRunnerConfig>()
            ?? throw new InvalidOperationException(
                "Failed to bind AgentRunnerConfig from IConfiguration. Ensure 'AgentRunner' section is present.");

        services
            .AddAgentRunnerCore(config)
            .AddAgentTools(config)
            .AddLlmExecution(config);

        // CLI uses console output — no SignalR hub
        services.AddSingleton<IAgentEventSink, ConsoleAgentEventSink>();
    })
    .Build();

// ─── Run ──────────────────────────────────────────────────────────────────────

var orchestrator = host.Services.GetRequiredService<ICycleOrchestrator>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await orchestrator.StartAsync(cts.Token);
Console.WriteLine("Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => Task.CompletedTask);
await orchestrator.StopAsync();
