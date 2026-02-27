using Microsoft.AspNetCore.Mvc;

namespace MarketDataService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubscriptionsController : ControllerBase
{
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(ILogger<SubscriptionsController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<List<string>> GetSubscriptions()
    {
        return Ok(new List<string> 
        { 
            "prices", 
            "trades", 
            "balance", 
            "orderbook" 
        });
    }
}
