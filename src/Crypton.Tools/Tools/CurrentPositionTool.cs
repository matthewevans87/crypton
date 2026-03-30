using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Retrieves current portfolio and position data from the Execution Service.</summary>
public sealed class CurrentPositionTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _executionServiceUrl;
    private readonly IToolExecutor _executor;

    public string Name => "current_position";
    public string Description => "Retrieves the current portfolio from the Execution Service: open positions, recent trades, and cash balance.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Retrieve the current portfolio state from the Execution Service. This is the ground truth for all portfolio-aware reasoning — call it early in your procedure.",
        Parameters: [],
        Returns:
            "JSON portfolio summary containing: `mode` (\"paper\" or \"live\"), `timestamp`, `total_value`, `available_capital`, " +
            "`total_exposure_pct`, `unrealized_pnl`, and a `positions` array. Each position includes `asset`, `direction` (`long`/`short`), " +
            "`entry_price`, `current_price`, `quantity`, `allocation_pct`, `unrealized_pnl`, `unrealized_pnl_pct`, `opened_at`.",
        Examples: ["{}"],
        Notes:
        [
            "Call this first in any analysis or synthesis step. Portfolio state changes the meaning of every signal.",
            "In paper mode, all values are simulated. The return structure is identical to live mode.",
            "If the Execution Service is unreachable, the tool returns a cached snapshot with a `stale_at` timestamp. Note this in your output.",
        ]);

    public CurrentPositionTool(HttpClient http, string executionServiceUrl, IToolExecutor executor)
    {
        _http = http;
        _executionServiceUrl = executionServiceUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
        => ExecuteCoreAsync(ct);

    [Description("Returns current portfolio state: open positions, recent trades, cash balance, and P&L.")]
    internal async Task<string> ExecuteCoreAsync(CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var response = await _http.GetAsync($"{_executionServiceUrl}/portfolio/summary", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }, cancellationToken);
    }

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(CurrentPositionTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings { }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync();
            Console.WriteLine(result);
            return 0;
        }
    }
}
