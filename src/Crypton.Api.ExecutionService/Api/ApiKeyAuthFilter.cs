using Crypton.Api.ExecutionService.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Api;

/// <summary>
/// Action filter that validates the X-Api-Key header for write (POST/PUT/DELETE) endpoints.
/// Skipped on GET endpoints.
/// </summary>
public sealed class ApiKeyAuthFilter : IActionFilter
{
    private readonly string _apiKey;

    public ApiKeyAuthFilter(IOptions<ExecutionServiceConfig> config)
    {
        _apiKey = config.Value.Api.ApiKey;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Request.Method == "GET") return;

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != _apiKey)
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing API key." });
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
