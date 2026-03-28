using AgentRunner.Abstractions;
using AgentRunner.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly ICycleOrchestrator _orchestrator;

    public HealthController(ICycleOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive" });

    [HttpGet("ready")]
    public IActionResult Ready()
    {
        var state = _orchestrator.CurrentState;
        var isReady = state != LoopState.Failed;
        return isReady
            ? Ok(new { status = "ready" })
            : StatusCode(503, new { status = "not ready", currentState = state.ToString() });
    }
}