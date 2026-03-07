using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Configuration;
using System.Text;
using System.Text.Json;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExecutionController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _executionServiceUrl;
    private readonly string _executionServiceApiKey;

    public ExecutionController(
        IHttpClientFactory httpClientFactory,
        MonitoringDashboardConfig configuration)
    {
        _httpClientFactory = httpClientFactory;
        _executionServiceUrl = configuration.ExecutionService.Url.TrimEnd('/');
        _executionServiceApiKey = configuration.ExecutionService.ApiKey;
    }

    private HttpClient CreateExecutionClient()
    {
        var client = _httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(_executionServiceApiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", _executionServiceApiKey);
        }

        return client;
    }

    private async Task<IActionResult> ProxyExecutionPostAsync(string relativePath, object? body, CancellationToken ct)
    {
        try
        {
            var client = CreateExecutionClient();
            var content = body is null
                ? null
                : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_executionServiceUrl}/{relativePath.TrimStart('/')}", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return StatusCode((int)response.StatusCode, new { status = response.StatusCode.ToString() });
            }

            return new ContentResult
            {
                Content = responseBody,
                ContentType = "application/json",
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"ExecutionService unavailable: {ex.Message}" });
        }
    }

    [HttpPost("degrade")]
    public async Task<IActionResult> Degrade([FromBody] ExecutionDegradeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "reason is required" });
        }

        return await ProxyExecutionPostAsync("api/control/degrade", request, ct);
    }

    [HttpPost("recover")]
    public Task<IActionResult> Recover(CancellationToken ct)
        => ProxyExecutionPostAsync("api/control/recover", null, ct);

    [HttpPost("promote-to-live")]
    public Task<IActionResult> PromoteToLive([FromBody] OperatorNoteRequest request, CancellationToken ct)
        => ProxyExecutionPostAsync("api/control/promote-to-live", request, ct);

    [HttpPost("demote-to-paper")]
    public Task<IActionResult> DemoteToPaper([FromBody] OperatorNoteRequest request, CancellationToken ct)
        => ProxyExecutionPostAsync("api/control/demote-to-paper", request, ct);

    [HttpPost("reload-strategy")]
    public Task<IActionResult> ReloadStrategy(CancellationToken ct)
        => ProxyExecutionPostAsync("api/control/reload-strategy", null, ct);
}

public sealed record OperatorNoteRequest(string? OperatorNote);
public sealed record ExecutionDegradeRequest(string Reason);
