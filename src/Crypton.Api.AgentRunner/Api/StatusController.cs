using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Mailbox;
using AgentRunner.Startup;
using AgentRunner.StateMachine;
using AgentRunner.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api")]
public class StatusController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;
    private readonly MetricsCollector _metrics;
    private readonly ServiceAvailabilityState _availabilityState;

    public StatusController(
        AgentRunnerService agentRunner,
        ArtifactManager artifactManager,
        MailboxManager mailboxManager,
        MetricsCollector metrics,
        ServiceAvailabilityState availabilityState)
    {
        _agentRunner = agentRunner;
        _artifactManager = artifactManager;
        _mailboxManager = mailboxManager;
        _metrics = metrics;
        _availabilityState = availabilityState;
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
            pauseReason = _agentRunner.CurrentCycle?.PauseReason,
            isDegraded = _availabilityState.IsDegraded,
            degradedErrors = _availabilityState.Errors,
            degradedSince = _availabilityState.LastTransitionAt
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
            {
                break;
            }
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