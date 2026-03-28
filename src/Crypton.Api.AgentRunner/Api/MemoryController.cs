using AgentRunner.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api/memory")]
public class MemoryController : ControllerBase
{
    private readonly IArtifactStore _artifacts;

    public MemoryController(IArtifactStore artifacts)
    {
        _artifacts = artifacts;
    }

    [HttpGet("{agent}")]
    public IActionResult GetMemory(string agent)
    {
        var memory = _artifacts.ReadMemory(agent);
        return Ok(new { agent, content = memory ?? "" });
    }

    [HttpGet("shared")]
    public IActionResult GetSharedMemory()
    {
        var memory = _artifacts.ReadSharedMemory();
        return Ok(new { content = memory ?? "" });
    }
}
