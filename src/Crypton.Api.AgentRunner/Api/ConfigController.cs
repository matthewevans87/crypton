using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api/config")]
[ApiKey]
public class ConfigController : ControllerBase
{
    private readonly ICycleOrchestrator _orchestrator;
    private readonly AgentRunnerConfig _config;

    public ConfigController(ICycleOrchestrator orchestrator, AgentRunnerConfig config)
    {
        _orchestrator = orchestrator;
        _config = config;
    }

    [HttpGet("cycle-interval")]
    public IActionResult GetCycleInterval()
    {
        return Ok(new
        {
            cycleIntervalMinutes = _config.Cycle.ScheduleIntervalMinutes,
            minInterval = _config.Cycle.MinDurationMinutes,
            maxInterval = _config.Cycle.MaxDurationMinutes,
        });
    }

    [HttpPost("cycle-interval")]
    public IActionResult SetCycleInterval([FromBody] CycleIntervalRequest request)
    {
        if (request.CycleIntervalMinutes <= 0)
            return BadRequest(new { error = "cycleIntervalMinutes must be positive." });

        if (request.CycleIntervalMinutes < _config.Cycle.MinDurationMinutes ||
            request.CycleIntervalMinutes > _config.Cycle.MaxDurationMinutes)
        {
            return BadRequest(new
            {
                error = $"cycleIntervalMinutes must be between {_config.Cycle.MinDurationMinutes} and {_config.Cycle.MaxDurationMinutes}.",
            });
        }

        _config.Cycle.ScheduleIntervalMinutes = request.CycleIntervalMinutes;
        return Ok(new { cycleIntervalMinutes = _config.Cycle.ScheduleIntervalMinutes });
    }

    [HttpGet("all")]
    public IActionResult GetAllConfig()
    {
        return Ok(new
        {
            cycle = _config.Cycle,
            resilience = _config.Resilience,
            agents = _config.Agents.ToDictionary(
                kvp => kvp.Key,
                kvp => new { model = kvp.Value.Model, timeoutMinutes = kvp.Value.TimeoutMinutes }),
            storage = _config.Storage,
            api = new { port = _config.Api.Port },
        });
    }
}

public class CycleIntervalRequest
{
    public int CycleIntervalMinutes { get; set; }
}
