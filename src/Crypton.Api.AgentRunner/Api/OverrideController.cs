using AgentRunner.Agents;
using AgentRunner.Startup;
using AgentRunner.StateMachine;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api/override")]
[Route("api/control")]
[ApiKey]
public class OverrideController : ControllerBase
{
    private readonly AgentRunnerService _agentRunner;
    private readonly AgentRunnerStartupCoordinator _startupCoordinator;
    private readonly ServiceAvailabilityState _availabilityState;

    public OverrideController(
        AgentRunnerService agentRunner,
        AgentRunnerStartupCoordinator startupCoordinator,
        ServiceAvailabilityState availabilityState)
    {
        _agentRunner = agentRunner;
        _startupCoordinator = startupCoordinator;
        _availabilityState = availabilityState;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        var result = await _startupCoordinator.TryStartAsync(HttpContext.RequestAborted);

        if (result.IsDegraded)
        {
            return StatusCode(503, new
            {
                message = result.Message,
                errors = result.Errors
            });
        }

        return Ok(new
        {
            message = result.Message
        });
    }

    [HttpPost("recover")]
    public Task<IActionResult> Recover() => Start();

    [HttpPost("degrade")]
    public IActionResult Degrade([FromBody] DegradeRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Reason))
        {
            return BadRequest(new { error = "reason is required" });
        }

        _availabilityState.EnterDegraded([$"manual: {body.Reason.Trim()}"]);

        return Ok(new
        {
            message = "Agent runner entered degraded mode",
            errors = _availabilityState.Errors
        });
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
    public async Task<IActionResult> Abort()
    {
        await _agentRunner.AbortAsync();
        return Ok(new { message = "Agent Runner aborted and reset" });
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

public class DegradeRequest
{
    public string? Reason { get; set; }
}