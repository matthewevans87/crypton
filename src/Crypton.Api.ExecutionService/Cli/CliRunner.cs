using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Cli;

/// <summary>
/// Entry point for one-shot CLI mode.
/// Builds a lightweight DI container (no HTTP server), runs the requested command, and exits.
///
/// Supported commands:
/// <list type="bullet">
///   <item><c>status</c> — print operation mode, safe-mode state, active strategy, open positions and P&amp;L.</item>
///   <item><c>safe-mode clear</c> — deactivate safe mode.</item>
///   <item><c>set-mode paper|live</c> — change and persist the operation mode.</item>
///   <item><c>promote-to-live</c> — alias for <c>set-mode live</c>.</item>
///   <item><c>demote-to-paper</c> — alias for <c>set-mode paper</c>.</item>
///   <item><c>run-order --symbol &lt;pair&gt; --side buy|sell --type market|limit [--quantity &lt;qty&gt;] [--price &lt;px&gt;] [--verbose]</c></item>
///   <item><c>reconcile</c> — trigger one reconciliation pass and print the outcome.</item>
/// </list>
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        // Normalise: strip --cli prefix if present and find the verb.
        var normalized = args
            .SkipWhile(a => a == "--cli")
            .ToArray();

        var verb = normalized.Length > 0 ? normalized[0] : null;
        var verbose = args.Contains("--verbose");

        // Build service provider from the full execution service DI registration.
        // Hosted services are registered but NOT started — we start only what we need.
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);

        builder.Services.AddExecutionServiceCore(builder.Configuration);

        await using var sp = builder.Services.BuildServiceProvider();

        try
        {
            return verb switch
            {
                "status"          => await StatusAsync(sp),
                "safe-mode"       => await SafeModeAsync(normalized, sp),
                "set-mode"        => await SetModeAsync(normalized, sp),
                "promote-to-live" => await SetModeAsync(["set-mode", "live"], sp),
                "demote-to-paper" => await SetModeAsync(["set-mode", "paper"], sp),
                "run-order"       => await RunOrderAsync(normalized, sp, verbose),
                "reconcile"       => await ReconcileAsync(sp),
                null or ""        => PrintHelp("No verb provided."),
                _                 => PrintHelp($"Unknown verb: {verb}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // status
    // ─────────────────────────────────────────────────────────────────────────

    private static Task<int> StatusAsync(IServiceProvider sp)
    {
        var modeService  = sp.GetRequiredService<IOperationModeService>();
        var safeMode     = sp.GetRequiredService<ISafeModeController>();
        var stratService = sp.GetRequiredService<IStrategyService>();
        var registry     = sp.GetRequiredService<PositionRegistry>();

        Console.WriteLine("══════════════════════════════════════════════");
        Console.WriteLine(" Execution Service — Status");
        Console.WriteLine("══════════════════════════════════════════════");
        Console.WriteLine($" Mode      : {modeService.CurrentMode.ToUpperInvariant()}");
        Console.WriteLine($" Safe mode : {(safeMode.IsActive ? $"ACTIVE — {safeMode.Reason}" : "inactive")}");

        // Strategy
        var strat = stratService.ActiveStrategy;
        if (strat is not null)
        {
            Console.WriteLine($" Strategy  : {strat.Id ?? "(no id)"} — {stratService.State}");
        }
        else
        {
            Console.WriteLine(" Strategy  : none loaded");
        }

        // Positions
        var positions = registry.OpenPositions;
        Console.WriteLine($" Positions : {positions.Count} open");

        if (positions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {"Asset",-12} {"Dir",-6} {"Qty",10} {"Entry",10} {"CostBasis",12}");
            Console.WriteLine($"  {new string('-', 54)}");
            foreach (var p in positions)
            {
                Console.WriteLine(
                    $"  {p.Asset,-12} {p.Direction,-6} {p.Quantity,10:F6} {p.AverageEntryPrice,10:F2} {(p.Quantity * p.AverageEntryPrice),12:F2}");
            }
        }

        // Closed trades summary
        var trades = registry.ClosedTrades;
        if (trades.Count > 0)
        {
            var totalPnl = trades.Sum(t => t.RealizedPnl);
            Console.WriteLine();
            Console.WriteLine($" Closed trades : {trades.Count}   Total realised P&L : {totalPnl:+0.00;-0.00} USD");
        }

        Console.WriteLine("══════════════════════════════════════════════");
        return Task.FromResult(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // safe-mode clear
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<int> SafeModeAsync(string[] args, IServiceProvider sp)
    {
        var subVerb = args.Length > 1 ? args[1] : null;
        if (subVerb != "clear")
            return PrintHelp("Usage: safe-mode clear");

        var safeMode = sp.GetRequiredService<ISafeModeController>();
        if (!safeMode.IsActive)
        {
            Console.WriteLine("[info] Safe mode is not active.");
            return 0;
        }

        await safeMode.DeactivateAsync();
        Console.WriteLine("[ok] Safe mode cleared.");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // set-mode paper|live
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<int> SetModeAsync(string[] args, IServiceProvider sp)
    {
        var targetMode = args.Length > 1 ? args[1].ToLowerInvariant() : null;
        if (targetMode is not ("paper" or "live"))
            return PrintHelp("Usage: set-mode paper|live");

        var modeService = sp.GetRequiredService<IOperationModeService>();
        if (modeService.CurrentMode == targetMode)
        {
            Console.WriteLine($"[info] Already in {targetMode} mode.");
            return 0;
        }

        if (targetMode == "live")
            await modeService.PromoteToLiveAsync("CLI promote");
        else
            await modeService.DemoteToPaperAsync("CLI demote");

        Console.WriteLine($"[ok] Switched to {targetMode.ToUpperInvariant()} mode.");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // run-order
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<int> RunOrderAsync(
        string[] args, IServiceProvider sp, bool verbose)
    {
        // Argument parsing
        string? symbol    = ArgValue(args, "--symbol");
        string? sideStr   = ArgValue(args, "--side");
        string? typeStr   = ArgValue(args, "--type");
        string? qtyStr    = ArgValue(args, "--quantity");
        string? priceStr  = ArgValue(args, "--price");

        if (symbol is null || sideStr is null || typeStr is null)
            return PrintHelp("Usage: run-order --symbol <pair> --side buy|sell --type market|limit [--quantity <qty>] [--price <px>]");

        if (!Enum.TryParse<OrderSide>(sideStr, ignoreCase: true, out var side))
            return PrintHelp($"Invalid side '{sideStr}'. Use: buy, sell");

        if (!Enum.TryParse<OrderType>(typeStr, ignoreCase: true, out var orderType))
            return PrintHelp($"Invalid type '{typeStr}'. Use: market, limit");

        var quantity = qtyStr is not null && decimal.TryParse(qtyStr, out var q) ? q : 0.001m;
        decimal? limitPrice = priceStr is not null && decimal.TryParse(priceStr, out var p) ? p : null;

        var modeService = sp.GetRequiredService<IOperationModeService>();
        var mode = modeService.CurrentMode;

        // For paper mode, we need market data snapshots to simulate fill prices.
        // Start the MarketDataServiceClient briefly before placing the order.
        MarketDataServiceClient? mdClient = null;
        if (mode == "paper")
        {
            mdClient = sp.GetRequiredService<MarketDataServiceClient>();
            Console.WriteLine("[info] Starting market data connection for paper-mode fill simulation…");
            using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await mdClient.StartAsync(startCts.Token);
                // Give the hub a moment to deliver the first snapshot.
                await Task.Delay(TimeSpan.FromSeconds(3), startCts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[warn] Could not connect to market data hub: {ex.Message}");
                Console.WriteLine("[warn] Proceeding — paper fill may use stale or zero price.");
            }
        }

        try
        {
            var router = sp.GetRequiredService<OrderRouter>();
            var posId = $"cli-{Guid.NewGuid():N}";

            Console.WriteLine($"[info] Placing {orderType} {side} order for {quantity} {symbol} (mode: {mode})…");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var record = await router.PlaceEntryOrderAsync(
                symbol, side, orderType, quantity, limitPrice,
                strategyPositionId: posId,
                mode: mode,
                strategyId: "cli",
                token: cts.Token);

            if (record is null)
            {
                Console.WriteLine("[warn] Order deduplicated or not submitted.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine($"  Internal ID    : {record.InternalId}");
            Console.WriteLine($"  Exchange ID    : {record.ExchangeOrderId ?? "(pending)"}");
            Console.WriteLine($"  Status         : {record.Status}");
            Console.WriteLine($"  Filled qty     : {record.FilledQuantity:F6}");
            Console.WriteLine($"  Avg fill price : {record.AverageFillPrice?.ToString("F2") ?? "—"}");

            if (record.Status == OrderStatus.Rejected)
            {
                Console.Error.WriteLine($"\n[error] Order rejected: {record.RejectionReason}");
                return 1;
            }

            Console.WriteLine("\n[ok] Order completed.");
            return 0;
        }
        finally
        {
            if (mdClient is not null)
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await mdClient.StopAsync(stopCts.Token);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // reconcile
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<int> ReconcileAsync(IServiceProvider sp)
    {
        var reconciler = sp.GetRequiredService<ReconciliationService>();

        Console.WriteLine("[info] Running reconciliation…");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await reconciler.StartAsync(cts.Token);

        // ReconciliationService.ReconciliationTask is internal; poll for completion.
        var deadline = DateTime.UtcNow.AddSeconds(55);
        while (reconciler.ReconciliationTask is { IsCompleted: false } &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, cts.Token);
        }

        if (reconciler.ReconciliationTask is { IsCompleted: false })
        {
            Console.WriteLine("[warn] Reconciliation did not complete within 55 s.");
        }
        else if (reconciler.ReconciliationTask?.IsFaulted == true)
        {
            Console.Error.WriteLine(
                $"[error] Reconciliation faulted: {reconciler.ReconciliationTask.Exception?.InnerException?.Message}");
            return 1;
        }
        else
        {
            Console.WriteLine("[ok] Reconciliation complete.");
        }

        await reconciler.StopAsync(cts.Token);
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int PrintHelp(string? message = null)
    {
        if (message is not null)
            Console.Error.WriteLine($"[error] {message}");

        Console.WriteLine("""
            Crypton Execution Service — CLI

            Usage:
              status                              Print mode, strategy, positions, P&L
              safe-mode clear                     Deactivate safe mode
              set-mode paper|live                 Change operation mode
              promote-to-live                     Alias for 'set-mode live'
              demote-to-paper                     Alias for 'set-mode paper'
              run-order --symbol <pair>           Place a single order
                        --side buy|sell
                        --type market|limit
                        [--quantity <qty>]
                        [--price <px>]
                        [--verbose]
              reconcile                           Run one reconciliation pass

            Flags:
              --env-file <path>    Load .env file from given path (~/… supported)
              --service            Force service mode (don't enter CLI mode)
              --verbose            Enable debug logging in CLI mode
            """);
        return message is null ? 0 : 1;
    }

    /// <summary>Returns the value of a named argument, e.g. --symbol BTC/USD → "BTC/USD".</summary>
    private static string? ArgValue(string[] args, string name)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}

