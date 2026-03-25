using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrategyController : ControllerBase
{
    private readonly IExecutionServiceClient _executionServiceClient;
    private readonly ILogger<StrategyController> _logger;

    public StrategyController(IExecutionServiceClient executionServiceClient, ILogger<StrategyController> logger)
    {
        _executionServiceClient = executionServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns the active strategy proxied from ExecutionService GET /strategy.
    /// Returns 200 with null body when no strategy is loaded yet (ExecutionService 404)
    /// so callers can distinguish "no strategy yet" from a service error.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var (statusCode, body) = await _executionServiceClient.GetStrategyAsync(ct);
        if (statusCode == 404)
            return Content("null", "application/json", System.Text.Encoding.UTF8);
        Response.StatusCode = statusCode;
        return Content(body, "application/json", System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// ExecutionService does not persist strategy history; returns empty list.
    /// </summary>
    [HttpGet("history")]
    public ActionResult<List<StrategyHistoryItem>> GetHistory([FromQuery] int limit = 20)
    {
        return Ok(new List<StrategyHistoryItem>());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var (statusCode, body) = await _executionServiceClient.GetStrategyAsync(ct);
        if (statusCode == 404)
            return Ok((object?)null);
        Response.StatusCode = statusCode;
        return Content(body, "application/json", System.Text.Encoding.UTF8);
    }
}
