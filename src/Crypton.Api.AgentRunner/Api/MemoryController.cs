using AgentRunner.Artifacts;
using Microsoft.AspNetCore.Mvc;

namespace AgentRunner.Api;

[ApiController]
[Route("api/memory")]
public class MemoryController : ControllerBase
{
    private readonly ArtifactManager _artifactManager;

    public MemoryController(ArtifactManager artifactManager)
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

        List<MemorySearchResult> results;
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