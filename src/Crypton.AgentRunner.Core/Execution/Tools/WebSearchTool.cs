using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Web search via the Brave Search API.</summary>
public sealed class WebSearchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IToolExecutor _executor;

    public string Name => "web_search";
    public string Description => "Performs a web search via the Brave Search API. Be specific — include asset names, dates, and key terms.";

    public WebSearchTool(HttpClient http, string apiKey, IToolExecutor executor)
    {
        _http = http;
        _apiKey = apiKey;
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Searches the web for current information about a topic. Returns titles, URLs, and descriptions.")]
    private async Task<string> ExecuteAsync(
        [Description("The search query. Include asset names, dates, and key terms.")] string query,
        [Description("Number of results (1–20).")] int count = 10,
        [Description("Age filter: day, week, month, year, any.")] string recency = "any",
        CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                return "Error: Brave Search API key not configured (Tools__BraveSearch__ApiKey).";

            count = Math.Clamp(count, 1, 20);
            var freshness = recency switch
            {
                "day" => "pd",
                "week" => "pw",
                "month" => "pm",
                "year" => "py",
                _ => null
            };

            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}";
            if (freshness is not null) url += $"&freshness={freshness}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", _apiKey);

            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(body);

            var results = new List<object>();
            if (doc.RootElement.TryGetProperty("web", out var web) &&
                web.TryGetProperty("results", out var arr))
            {
                foreach (var r in arr.EnumerateArray())
                    results.Add(new
                    {
                        title = r.TryGetProperty("title", out var t) ? t.GetString() : "",
                        url = r.TryGetProperty("url", out var u) ? u.GetString() : "",
                        description = r.TryGetProperty("description", out var d) ? d.GetString() : ""
                    });
            }

            return JsonSerializer.Serialize(results);
        }, cancellationToken);
    }
}
