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
            ["query"]   = new ToolParameterProperty { Type = "string",  Description = "The search query. Be specific; include asset names, dates, and key terms." },
            ["count"]   = new ToolParameterProperty { Type = "integer", Description = "Number of results (1-20)", Default = 10 },
            ["recency"] = new ToolParameterProperty { Type = "string",  Description = "Age filter: day, week, month, year, any", Default = "any" },
            ["market"]  = new ToolParameterProperty { Type = "string",  Description = "Locale string for regional filtering", Default = "en-US" }
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
        var query = parameters.GetString("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'query' parameter" };
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return new ToolResult { Success = false, Error = "Brave Search API key not configured. Set env var BRAVE_SEARCH_API_KEY." };
        }

        var numResults = parameters.ContainsKey("count")

            ? parameters.GetInt("count", 10)
            : parameters.GetInt("num_results", 10);
        numResults = Math.Clamp(numResults, 1, 20);

        // Map recency string to Brave freshness param
        var recency = parameters.GetString("recency") ?? "any";
        var freshness = recency switch
        {
            "day"   => "pd",
            "week"  => "pw",
            "month" => "pm",
            "year"  => "py",
            _       => null
        };

        try
        {
            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={numResults}";
            if (freshness != null) url += $"&freshness={freshness}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
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
