using MarketDataService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketDataService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(IMetricsCollector metricsCollector, ILogger<MetricsController> logger)
    {
        _metricsCollector = metricsCollector;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<MetricsResponse> GetMetrics()
    {
        try
        {
            var metrics = _metricsCollector.GetMetrics();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics");
            return StatusCode(500, new { error = "Failed to retrieve metrics" });
        }
    }

    [HttpGet("health")]
    public ActionResult GetHealth()
    {
        var metrics = _metricsCollector.GetMetrics();
        
        if (!metrics.IsHealthy)
        {
            return StatusCode(503, new { 
                status = "unhealthy", 
                alerts = metrics.ActiveAlerts 
            });
        }
        
        return Ok(new { status = "healthy" });
    }
}
