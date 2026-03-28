using AgentRunner.Abstractions;
using AgentRunner.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api")]
public class StatusController : ControllerBase
{
    private readonly ICycleOrchestrator _orchestrator;
    private readonly IArtifactStore _artifacts;

    public StatusController(ICycleOrchestrator orchestrator, IArtifactStore artifacts)
    {
        _orchestrator = orchestrator;
        _artifacts = artifacts;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            currentState = _orchestrator.CurrentState.ToString(),
            currentCycleId = _orchestrator.CurrentCycle?.CycleId,
            lastCompletedStep = _orchestrator.CurrentCycle?.Steps
                .Where(s => s.Value.EndTime.HasValue)
                .OrderByDescending(s => s.Value.EndTime)
                .Select(s => s.Key.ToString())
                .FirstOrDefault(),
            nextScheduledTime = _orchestrator.CurrentState == LoopState.WaitingForNextCycle
                ? (DateTimeOffset?)_orchestrator.NextScheduledAt
                : null,
            isPaused = _orchestrator.CurrentCycle?.IsPaused ?? false,
            pauseReason = _orchestrator.CurrentCycle?.PauseReason,
            restartCount = _orchestrator.RestartCount,
        });
    }

    [HttpGet("cycles")]
    public IActionResult GetCycles([FromQuery] int count = 10)
    {
        var cycles = _artifacts.GetRecentCycleIds(count);
        return Ok(cycles.Select(c => new
        {
            cycleId = c,
            artifacts = _artifacts.GetCycleArtifactNames(c),
        }));
    }

    [HttpGet("cycles/{cycleId}")]
    public IActionResult GetCycleDetails(string cycleId)
    {
        var names = _artifacts.GetCycleArtifactNames(cycleId);
        if (names.Count == 0)
            return NotFound(new { error = "Cycle not found" });

        var artifactMap = names.ToDictionary(
            name => name,
            name => _artifacts.Read(cycleId, name));

        return Ok(new { cycleId, artifacts = artifactMap });
    }
}
