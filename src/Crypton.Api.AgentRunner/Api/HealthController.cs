using AgentRunner.Agents;
using AgentRunner.Startup;
using AgentRunner.StateMachine;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;
    private readonly ServiceAvailabilityState _availabilityState;

    public HealthController(
        AgentRunnerService agentRunner,
        ServiceAvailabilityState availabilityState)
    {
        _agentRunner = agentRunner;
        _availabilityState = availabilityState;
    }

    [HttpGet("live")]
    public IActionResult Live()
    {
        return Ok(new { status = "alive" });
    }

    [HttpGet("ready")]
    public IActionResult Ready()
    {
        if (_availabilityState.IsDegraded)
        {
            return StatusCode(503, new
            {
                status = "not ready",
                reason = "degraded",
                errors = _availabilityState.Errors
            });
        }

        var state = _agentRunner.CurrentState;
        var isReady = state != LoopState.Failed;

        return isReady
            ? Ok(new { status = "ready" })
            : StatusCode(503, new { status = "not ready", currentState = state.ToString() });
    }
}