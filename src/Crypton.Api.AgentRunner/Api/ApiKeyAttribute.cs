using AgentRunner.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgentRunner.Api;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<AgentRunnerConfig>();

        // If no API key is configured, auth is disabled — allow all requests.
        if (string.IsNullOrEmpty(config.Api.ApiKey))
        {
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKey)
            || apiKey != config.Api.ApiKey)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}