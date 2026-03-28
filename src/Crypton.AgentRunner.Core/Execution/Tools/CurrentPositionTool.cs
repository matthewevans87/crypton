using System.ComponentModel;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Retrieves current portfolio and position data from the Execution Service.</summary>
public sealed class CurrentPositionTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _executionServiceUrl;
    private readonly IToolExecutor _executor;

    public string Name => "current_position";
    public string Description => "Retrieves the current portfolio from the Execution Service: open positions, recent trades, and cash balance.";

    public CurrentPositionTool(HttpClient http, string executionServiceUrl, IToolExecutor executor)
    {
        _http = http;
        _executionServiceUrl = executionServiceUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Returns current portfolio state: open positions, recent trades, cash balance, and P&L.")]
    private async Task<string> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var response = await _http.GetAsync($"{_executionServiceUrl}/portfolio/summary", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }, cancellationToken);
    }
}
