using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Positions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Resilience;

/// <summary>
/// Runs once on service startup to reconcile the in-process <see cref="PositionRegistry"/>
/// against the real positions held on the exchange.
/// - Orphaned registry positions (in registry, NOT on exchange) are closed with reason "reconciled_missing".
/// - Unknown exchange positions (on exchange, NOT in registry) are added with Origin = "reconciled".
/// Skipped entirely if <see cref="FailureTracker.SafeModeTriggered"/> is true.
/// </summary>
public sealed class ReconciliationService : IHostedService
{
    private readonly IExchangeAdapter _exchange;
    private readonly PositionRegistry _registry;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<ReconciliationService> _logger;
    private readonly FailureTracker _failureTracker;
    private CancellationTokenSource? _cts;

    /// <summary>Exposed for test awaiting; null until StartAsync is called.</summary>
    internal Task? ReconciliationTask { get; private set; }

    public ReconciliationService(
        IExchangeAdapter exchange,
        PositionRegistry registry,
        IEventLogger eventLogger,
        ILogger<ReconciliationService> logger,
        FailureTracker failureTracker)
    {
        _exchange = exchange;
        _registry = registry;
        _eventLogger = eventLogger;
        _logger = logger;
        _failureTracker = failureTracker;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ReconciliationTask = Task.Run(() => RunReconciliationAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (ReconciliationTask is not null)
        {
            try
            {
                await ReconciliationTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunReconciliationAsync(CancellationToken ct)
    {
        if (_failureTracker.SafeModeTriggered)
        {
            _logger.LogWarning("Reconciliation skipped: safe mode is active.");
            return;
        }

        _logger.LogInformation("Starting post-restart reconciliation…");
        int orphaned = 0, unknown = 0, matched = 0;

        IReadOnlyList<global::Crypton.Api.ExecutionService.Models.ExchangePosition> exchangePositions;
        try
        {
            exchangePositions = await _exchange.GetOpenPositionsAsync(ct);
        }
        catch (ExchangeAdapterException ex)
        {
            _logger.LogError(ex, "Reconciliation failed: exchange adapter error. Continuing without reconciliation.");
            await _eventLogger.LogAsync(EventTypes.ReconciliationSummary, "reconciliation", new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["error"] = ex.Message
            }, ct);
            return;
        }

        var registryPositions = _registry.OpenPositions;

        // Build lookup sets by (Asset, Direction)
        var registryKeys = new HashSet<string>(
            registryPositions.Select(p => PositionKey(p.Asset, p.Direction)));
        var exchangeKeys = new HashSet<string>(
            exchangePositions.Select(p => PositionKey(p.Asset, p.Direction)));

        // Orphaned: in registry but NOT on exchange
        foreach (var pos in registryPositions)
        {
            if (!exchangeKeys.Contains(PositionKey(pos.Asset, pos.Direction)))
            {
                _registry.ClosePosition(pos.Id, pos.AverageEntryPrice, "reconciled_missing");

                await _eventLogger.LogAsync(EventTypes.PositionClosed, "reconciliation",
                    new Dictionary<string, object?>
                    {
                        ["position_id"] = pos.Id,
                        ["asset"] = pos.Asset,
                        ["direction"] = pos.Direction,
                        ["exit_reason"] = "reconciled_missing",
                        ["origin"] = "reconciliation"
                    }, ct);

                _logger.LogWarning(
                    "Orphaned position {Id} ({Asset} {Direction}) not found on exchange — closed with reason 'reconciled_missing'.",
                    pos.Id, pos.Asset, pos.Direction);
                orphaned++;
            }
            else
            {
                matched++;
            }
        }

        // Unknown: on exchange but NOT in registry
        foreach (var ep in exchangePositions)
        {
            if (!registryKeys.Contains(PositionKey(ep.Asset, ep.Direction)))
            {
                var newPos = new global::Crypton.Api.ExecutionService.Positions.OpenPosition
                {
                    Id = Guid.NewGuid().ToString("N"),
                    StrategyPositionId = $"reconciled_{ep.Asset}_{ep.Direction}",
                    StrategyId = "reconciled",
                    Asset = ep.Asset,
                    Direction = ep.Direction,
                    Quantity = ep.Quantity,
                    AverageEntryPrice = ep.AverageEntryPrice,
                    OpenedAt = DateTimeOffset.UtcNow,
                    Origin = "reconciled"
                };

                _registry.UpsertPosition(newPos);

                await _eventLogger.LogAsync(EventTypes.PositionOpened, "reconciliation",
                    new Dictionary<string, object?>
                    {
                        ["position_id"] = newPos.Id,
                        ["asset"] = ep.Asset,
                        ["direction"] = ep.Direction,
                        ["quantity"] = (double)ep.Quantity,
                        ["entry_price"] = (double)ep.AverageEntryPrice,
                        ["origin"] = "reconciliation"
                    }, ct);

                _logger.LogWarning(
                    "Unknown position {Asset} {Direction} found on exchange — added to registry with Origin='reconciled'.",
                    ep.Asset, ep.Direction);
                unknown++;
            }
        }

        await _eventLogger.LogAsync(EventTypes.ReconciliationSummary, "reconciliation",
            new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["orphaned_closed"] = orphaned,
                ["unknown_added"] = unknown,
                ["matched"] = matched
            }, ct);

        _logger.LogInformation(
            "Reconciliation complete. Orphaned closed: {Orphaned}, Unknown added: {Unknown}, Matched: {Matched}.",
            orphaned, unknown, matched);
    }

    private static string PositionKey(string asset, string direction) => $"{asset}:{direction}";
}
