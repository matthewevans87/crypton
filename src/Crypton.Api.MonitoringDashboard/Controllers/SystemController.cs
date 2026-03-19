using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;

namespace MonitoringDashboard.Controllers;

/// <summary>
/// Aggregates live health + state from every upstream service into a single response
/// for the "System Diagnostics" panel and the status-bar service chips.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly ISystemHealthChecker _healthChecker;

    public SystemController(ISystemHealthChecker healthChecker)
    {
        _healthChecker = healthChecker;
    }

    /// <summary>
    /// Returns current status for MarketData, ExecutionService, and AgentRunner.
    /// Each service check runs in parallel with a 4-second timeout.
    /// Never throws to the caller — offline services return status:"offline".
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SystemStatus>> GetStatus(CancellationToken requestCt)
    {
        var correlationId = HttpContext.TraceIdentifier;
        return Ok(await _healthChecker.GetStatusAsync(requestCt, correlationId));
    }
}
