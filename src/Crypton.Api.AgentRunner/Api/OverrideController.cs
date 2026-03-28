using AgentRunner.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api/override")]
[Route("api/control")]
[ApiKey]
public class OverrideController : ControllerBase
{
    private readonly ICycleOrchestrator _orchestrator;

    public OverrideController(ICycleOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        await _orchestrator.StartAsync(HttpContext.RequestAborted);
        return Ok(new { message = "Agent Runner started." });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        await _orchestrator.StopAsync();
        return Ok(new { message = "Agent Runner stopped." });
    }

    [HttpPost("pause")]
    public async Task<IActionResult> Pause([FromBody] PauseRequest? request)
    {
        await _orchestrator.PauseAsync(request?.Reason);
        return Ok(new { message = "Agent Runner paused", reason = request?.Reason });
    }

    [HttpPost("resume")]
    public async Task<IActionResult> Resume()
    {
        await _orchestrator.ResumeAsync();
        return Ok(new { message = "Agent Runner resumed." });
    }

    [HttpPost("abort")]
    public async Task<IActionResult> Abort()
    {
        await _orchestrator.AbortAsync();
        return Ok(new { message = "Agent Runner aborted and reset." });
    }

    [HttpPost("force-cycle")]
    public IActionResult ForceCycle()
    {
        _orchestrator.ForceNewCycle();
        return Ok(new { message = "New cycle triggered." });
    }

    [HttpPost("inject")]
    public IActionResult Inject([FromBody] InjectRequest request)
    {
        if (string.IsNullOrEmpty(request.Agent) || string.IsNullOrEmpty(request.Content))
            return BadRequest(new { error = "Agent and Content are required." });

        _orchestrator.InjectContext(request.Agent, request.Content);
        return Ok(new { message = $"Context injected to {request.Agent}." });
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
