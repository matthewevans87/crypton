using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using System.Text.Json;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly IAgentRunnerClient _agentRunnerClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _agentRunnerUrl;

    public AgentController(
        IAgentRunnerClient agentRunnerClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _agentRunnerClient = agentRunnerClient;
        _httpClientFactory = httpClientFactory;
        _agentRunnerUrl = (configuration["AgentRunner:Url"] ?? "http://localhost:5003").TrimEnd('/');
    }

    /// <summary>
    /// Returns the current agent state from AgentRunner GET /api/status.
    /// </summary>
    [HttpGet("state")]
    public async Task<ActionResult<AgentState>> GetState(CancellationToken ct)
    {
        var status = await _agentRunnerClient.GetStatusAsync(ct);
        if (status is null)
            return StatusCode(502, new { error = "AgentRunner unavailable" });

        var currentState = status.Value.TryGetProperty("currentState", out var cs) ? cs.GetString() : "Unknown";
        var isRunning = currentState is not (null or "WaitingForNextCycle" or "Idle");

        return Ok(new AgentState
        {
            CurrentState = currentState ?? "Unknown",
            IsRunning = isRunning,
            StateStartedAt = DateTime.UtcNow,   // not available from REST
            ProgressPercent = 0
        });
    }

    /// <summary>
    /// Returns loop status derived from AgentRunner GET /api/status and GET /api/cycles.
    /// </summary>
    [HttpGet("loop")]
    public async Task<ActionResult<LoopStatus>> GetLoopStatus(CancellationToken ct)
    {
        var statusTask = _agentRunnerClient.GetStatusAsync(ct);
        var cyclesTask = _agentRunnerClient.GetCyclesAsync(20, ct);
        await Task.WhenAll(statusTask, cyclesTask);

        var status = statusTask.Result;
        if (status is null)
            return StatusCode(502, new { error = "AgentRunner unavailable" });

        var currentState = status.Value.TryGetProperty("currentState", out var cs) ? cs.GetString() : "Unknown";
        var isRunning = currentState is not (null or "WaitingForNextCycle" or "Idle");

        DateTime? nextScheduled = null;
        if (status.Value.TryGetProperty("nextScheduledTime", out var ns) && ns.ValueKind != JsonValueKind.Null)
            nextScheduled = ns.GetDateTimeOffset().UtcDateTime;

        var cycleNumber = cyclesTask.Result?.ValueKind == JsonValueKind.Array
            ? cyclesTask.Result.Value.GetArrayLength() : 0;

        var lastStep = status.Value.TryGetProperty("lastCompletedStep", out var lcs) && lcs.ValueKind != JsonValueKind.Null
            ? lcs.GetString() : null;

        var currentArtifact = lastStep switch
        {
            "Plan" => "plan.md",
            "Research" => "research.md",
            "Analyze" => "analysis.md",
            "Synthesize" => "strategy.json",
            "Evaluate" => "evaluation.md",
            _ => null
        };

        return Ok(new LoopStatus
        {
            AgentState = new AgentState
            {
                CurrentState = currentState ?? "Unknown",
                IsRunning = isRunning,
                StateStartedAt = DateTime.UtcNow,
                ProgressPercent = 0
            },
            NextCycleExpectedAt = nextScheduled,
            CurrentArtifact = currentArtifact,
            CycleNumber = cycleNumber
        });
    }

    /// <summary>
    /// Tool call traces are not persisted as structured data by AgentRunner; returns empty list.
    /// </summary>
    [HttpGet("toolcalls")]
    public ActionResult<List<ToolCall>> GetToolCalls([FromQuery] int limit = 20)
    {
        return Ok(new List<ToolCall>());
    }

    /// <summary>
    /// Reasoning steps are not persisted as structured data by AgentRunner; returns empty list.
    /// </summary>
    [HttpGet("reasoning")]
    public ActionResult<List<ReasoningStep>> GetReasoning()
    {
        return Ok(new List<ReasoningStep>());
    }

    // -----------------------------------------------------------------------
    // Cycle interval — proxied to AgentRunner
    // -----------------------------------------------------------------------

    [HttpGet("config/cycle-interval")]
    public async Task<IActionResult> GetCycleInterval(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync($"{_agentRunnerUrl}/api/config/cycle-interval", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return Content(body, "application/json", System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"AgentRunner unavailable: {ex.Message}" });
        }
    }

    [HttpPost("config/cycle-interval")]
    public async Task<IActionResult> SetCycleInterval([FromBody] CycleIntervalRequest request, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(request,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }),
                System.Text.Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync($"{_agentRunnerUrl}/api/config/cycle-interval", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return StatusCode((int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"AgentRunner unavailable: {ex.Message}" });
        }
    }
}
