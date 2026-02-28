using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Execution;

/// <summary>
/// The main coordination service. Subscribes to market data ticks and
/// drives the entry and exit evaluators on every snapshot.
/// </summary>
public sealed class ExecutionEngine : IHostedService, IDisposable
{
    private readonly MarketDataHub _marketDataHub;
    private readonly EntryEvaluator _entryEvaluator;
    private readonly ExitEvaluator _exitEvaluator;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<ExecutionEngine> _logger;

    public string CurrentMode { get; private set; } = "paper";

    public ExecutionEngine(
        MarketDataHub marketDataHub,
        EntryEvaluator entryEvaluator,
        ExitEvaluator exitEvaluator,
        IEventLogger eventLogger,
        ILogger<ExecutionEngine> logger)
    {
        _marketDataHub = marketDataHub;
        _entryEvaluator = entryEvaluator;
        _exitEvaluator = exitEvaluator;
        _eventLogger = eventLogger;
        _logger = logger;

        _marketDataHub.OnSnapshot += OnSnapshotAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _eventLogger.LogAsync(EventTypes.ServiceStarted, CurrentMode);
        _logger.LogInformation("Execution Engine started in {Mode} mode", CurrentMode);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _eventLogger.LogAsync(EventTypes.ServiceStopped, CurrentMode);
    }

    private async Task OnSnapshotAsync(MarketSnapshot snapshot)
    {
        var snapshots = _marketDataHub.GetAllSnapshots();
        try
        {
            await _entryEvaluator.EvaluateAsync(snapshots, CurrentMode);
            await _exitEvaluator.EvaluateAsync(snapshots, CurrentMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation error on tick for {Asset}", snapshot.Asset);
        }
    }

    public void Dispose() { }
}
