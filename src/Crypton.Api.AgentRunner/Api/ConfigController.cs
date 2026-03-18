using AgentRunner.Agents;
using AgentRunner.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

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