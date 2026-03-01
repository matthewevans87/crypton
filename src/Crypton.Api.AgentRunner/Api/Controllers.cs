using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using AgentRunner.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgentRunner.Api;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var config = context.HttpContext.RequestServices.GetRequiredService<AgentRunnerConfig>();

        if (string.IsNullOrEmpty(config.Api.ApiKey))
        {
            return;
        }

        if (apiKey != config.Api.ApiKey)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}

[ApiController]
[Route("api")]
public class StatusController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;
    private readonly Artifacts.ArtifactManager _artifactManager;
    private readonly Mailbox.MailboxManager _mailboxManager;
    private readonly MetricsCollector _metrics;

    public StatusController(
        AgentRunnerService agentRunner,
        Artifacts.ArtifactManager artifactManager,
        Mailbox.MailboxManager mailboxManager,
        MetricsCollector metrics)
    {
        _agentRunner = agentRunner;
        _artifactManager = artifactManager;
        _mailboxManager = mailboxManager;
        _metrics = metrics;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            currentState = _agentRunner.CurrentState.ToString(),
            currentCycleId = _agentRunner.CurrentCycle?.CycleId,
            lastCompletedStep = _agentRunner.CurrentCycle?.Steps
                .Where(s => s.Value.EndTime.HasValue)
                .OrderByDescending(s => s.Value.EndTime)
                .FirstOrDefault().Key,
            nextScheduledTime = _agentRunner.CurrentState == LoopState.WaitingForNextCycle
                ? _agentRunner.NextScheduledRunTime
                : (DateTime?)null,
            isPaused = _agentRunner.CurrentCycle?.IsPaused ?? false,
            pauseReason = _agentRunner.CurrentCycle?.PauseReason
        });
    }

    [HttpGet("cycles")]
    public IActionResult GetCycles([FromQuery] int count = 10)
    {
        var cycles = _artifactManager.GetRecentCycles(count);
        return Ok(cycles.Select(c => new
        {
            cycleId = c,
            path = _artifactManager.GetCycleDirectory(c),
            artifacts = Directory.GetFiles(_artifactManager.GetCycleDirectory(c))
                .Select(Path.GetFileName)
        }));
    }

    [HttpGet("cycles/{cycleId}")]
    public IActionResult GetCycleDetails(string cycleId)
    {
        var cycleDir = _artifactManager.GetCycleDirectory(cycleId);
        if (!Directory.Exists(cycleDir))
        {
            return NotFound(new { error = "Cycle not found" });
        }

        var artifacts = new Dictionary<string, string?>();
        foreach (var file in Directory.GetFiles(cycleDir))
        {
            artifacts[Path.GetFileName(file)] = System.IO.File.ReadAllText(file);
        }

        return Ok(new
        {
            cycleId,
            artifacts
        });
    }

    [HttpGet("errors")]
    public IActionResult GetErrors([FromQuery] int count = 20)
    {
        var cycles = _artifactManager.GetRecentCycles(count * 2);
        var errors = new List<object>();

        var currentCycle = _agentRunner.CurrentCycle;

        foreach (var cycleId in cycles)
        {
            if (currentCycle?.CycleId == cycleId)
            {
                foreach (var step in currentCycle.Steps)
                {
                    if (step.Value.Outcome == StepOutcome.Failed || step.Value.Outcome == StepOutcome.Timeout)
                    {
                        errors.Add(new
                        {
                            cycleId,
                            step = step.Key,
                            outcome = step.Value.Outcome.ToString(),
                            error = step.Value.ErrorMessage,
                            timestamp = step.Value.StartTime
                        });
                    }
                }
            }

            if (errors.Count >= count)
                break;
        }

        return Ok(errors);
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        return Ok(new
        {
            cycleCount = _metrics.GetCycleCount(),
            stepSuccess = _metrics.GetStepSuccess(),
            stepFailure = _metrics.GetStepFailure(),
            toolExecution = _metrics.GetToolExecution()
        });
    }

    [HttpGet("mailboxes")]
    public IActionResult GetMailboxes()
    {
        return Ok(_mailboxManager.GetAllMailboxContents()
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(m => new
                {
                    from = m.FromAgent,
                    to = m.ToAgent,
                    content = m.Content,
                    timestamp = m.Timestamp,
                    type = m.Type.ToString()
                })));
    }
}

[ApiController]
[Route("api/override")]
[ApiKey]
public class OverrideController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;

    public OverrideController(AgentRunnerService agentRunner)
    {
        _agentRunner = agentRunner;
    }

    [HttpPost("pause")]
    public IActionResult Pause([FromBody] PauseRequest? request)
    {
        _agentRunner.Pause(request?.Reason);
        return Ok(new { message = "Agent Runner paused", reason = request?.Reason });
    }

    [HttpPost("resume")]
    public IActionResult Resume()
    {
        _agentRunner.Resume();
        return Ok(new { message = "Agent Runner resumed" });
    }

    [HttpPost("abort")]
    public IActionResult Abort()
    {
        _agentRunner.Abort();
        return Ok(new { message = "Agent Runner cycle aborted" });
    }

    [HttpPost("force-cycle")]
    public IActionResult ForceCycle()
    {
        _agentRunner.ForceNewCycle();
        return Ok(new { message = "New cycle triggered" });
    }

    [HttpPost("inject")]
    public IActionResult Inject([FromBody] InjectRequest request)
    {
        if (string.IsNullOrEmpty(request.Agent) || string.IsNullOrEmpty(request.Content))
        {
            return BadRequest(new { error = "Agent and Content are required" });
        }

        _agentRunner.InjectContext(request.Agent, request.Content);
        return Ok(new { message = $"Context injected to {request.Agent}" });
    }
}

public class PauseRequest
{
    public string? Reason { get; set; }
}

public class InjectRequest
{
    public string Agent { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;

    public HealthController(AgentRunnerService agentRunner)
    {
        _agentRunner = agentRunner;
    }

    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new { status = "alive" });
    }

    [HttpGet("ready")]
    public IActionResult Ready()
    {
        var state = _agentRunner.CurrentState;
        var isReady = state != LoopState.Failed;

        return isReady
            ? Ok(new { status = "ready" })
            : StatusCode(503, new { status = "not ready", currentState = state.ToString() });
    }
}

[ApiController]
[Route("api/config")]
[ApiKey]
public class ConfigController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;
    private readonly AgentRunnerConfig _config;

    public ConfigController(AgentRunnerService agentRunner, AgentRunnerConfig config)
    {
        _agentRunner = agentRunner;
        _config = config;
    }

    [HttpGet("cycle-interval")]
    public IActionResult GetCycleInterval()
    {
        return Ok(new
        {
            cycleIntervalMinutes = _config.Cycle.ScheduleIntervalMinutes,
            minInterval = _config.Cycle.MinDurationMinutes,
            maxInterval = _config.Cycle.MaxDurationMinutes
        });
    }

    [HttpPost("cycle-interval")]
    public IActionResult SetCycleInterval([FromBody] CycleIntervalRequest request)
    {
        if (request.CycleIntervalMinutes <= 0)
        {
            return BadRequest(new { error = "cycleIntervalMinutes must be positive" });
        }

        if (request.CycleIntervalMinutes < _config.Cycle.MinDurationMinutes ||
            request.CycleIntervalMinutes > _config.Cycle.MaxDurationMinutes)
        {
            return BadRequest(new
            {
                error = $"cycleIntervalMinutes must be between {_config.Cycle.MinDurationMinutes} and {_config.Cycle.MaxDurationMinutes}"
            });
        }

        _config.Cycle.ScheduleIntervalMinutes = request.CycleIntervalMinutes;

        return Ok(new
        {
            cycleIntervalMinutes = _config.Cycle.ScheduleIntervalMinutes,
            message = "Cycle interval updated. Changes will take effect on next cycle."
        });
    }

    [HttpGet("resilience")]
    public IActionResult GetResilienceConfig()
    {
        return Ok(_config.Resilience);
    }

    [HttpGet("all")]
    public IActionResult GetAllConfig()
    {
        return Ok(new
        {
            cycle = _config.Cycle,
            resilience = _config.Resilience,
            agents = new
            {
                plan = new { model = _config.Agents.Plan.Model, timeoutMinutes = _config.Agents.Plan.TimeoutMinutes },
                research = new { model = _config.Agents.Research.Model, timeoutMinutes = _config.Agents.Research.TimeoutMinutes },
                analyze = new { model = _config.Agents.Analyze.Model, timeoutMinutes = _config.Agents.Analyze.TimeoutMinutes },
                synthesis = new { model = _config.Agents.Synthesis.Model, timeoutMinutes = _config.Agents.Synthesis.TimeoutMinutes },
                evaluation = new { model = _config.Agents.Evaluation.Model, timeoutMinutes = _config.Agents.Evaluation.TimeoutMinutes }
            },
            storage = _config.Storage,
            api = new { port = _config.Api.Port }
        });
    }
}

public class CycleIntervalRequest
{
    public int CycleIntervalMinutes { get; set; }
}

[ApiController]
[Route("api/memory")]
public class MemoryController : ControllerBase
{
    private readonly Artifacts.ArtifactManager _artifactManager;

    public MemoryController(Artifacts.ArtifactManager artifactManager)
    {
        _artifactManager = artifactManager;
    }

    [HttpGet("{agent}")]
    public IActionResult GetMemory(string agent)
    {
        var memory = _artifactManager.ReadMemory(agent);
        return Ok(new { agent, content = memory ?? "" });
    }

    [HttpPost("{agent}/append")]
    public IActionResult AppendToMemory(string agent, [FromBody] MemoryAppendRequest request)
    {
        if (string.IsNullOrEmpty(request.Content))
        {
            return BadRequest(new { error = "Content is required" });
        }

        var contentWithTimestamp = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {request.Content}";
        _artifactManager.AppendToMemory(agent, contentWithTimestamp);

        return Ok(new { message = $"Appended to {agent} memory", agent });
    }

    [HttpGet("search")]
    public IActionResult SearchMemory([FromQuery] string query, [FromQuery] string? agent = null, [FromQuery] int maxResults = 10)
    {
        if (string.IsNullOrEmpty(query))
        {
            return BadRequest(new { error = "Query is required" });
        }

        List<Artifacts.MemorySearchResult> results;
        if (!string.IsNullOrEmpty(agent))
        {
            results = _artifactManager.SearchMemory(agent, query, maxResults);
        }
        else
        {
            results = _artifactManager.SearchAllMemory(query, maxResults);
        }

        return Ok(results.Select(r => new
        {
            r.AgentName,
            r.Content,
            r.MatchedQuery,
            r.Timestamp
        }));
    }
}

public class MemoryAppendRequest
{
    public string Content { get; set; } = string.Empty;
}
