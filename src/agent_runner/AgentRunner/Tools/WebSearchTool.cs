using System.Net.Http.Json;
using System.Text.Json;

namespace AgentRunner.Tools;

public class WebSearchTool : Tool
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public override string Name => "web_search";
    public override string Description => "Performs a web search via the Brave Search API";
    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["query"] = new ToolParameterProperty { Type = "string", Description = "The search query" },
            ["num_results"] = new ToolParameterProperty { Type = "integer", Description = "Number of results to return", Default = 10 },
            ["recency"] = new ToolParameterProperty { Type = "string", Description = "Recency filter (e.g., '24h', '7d', '30d')" }
        },
        Required = new List<string> { "query" }
    };

    public WebSearchTool(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("query", out var queryObj) || queryObj is not string query)
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'query' parameter" };
        }

        var numResults = 10;
        if (parameters.TryGetValue("num_results", out var numObj) && numObj is JsonElement numElement)
        {
            numResults = numElement.GetInt32();
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, 
                $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={numResults}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", _apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new ToolResult { Success = false, Error = $"Brave API error: {response.StatusCode}" };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = JsonSerializer.Deserialize<JsonElement>(content);

            var webResults = new List<WebSearchResult>();
            if (results.TryGetProperty("web", out var webResultsElement) && 
                webResultsElement.TryGetProperty("results", out var resultsArray))
            {
                foreach (var result in resultsArray.EnumerateArray())
                {
                    webResults.Add(new WebSearchResult
                    {
                        Title = result.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                        Url = result.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                        Description = result.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                    });
                }
            }

            return new ToolResult { Success = true, Data = webResults };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}

public class WebSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
