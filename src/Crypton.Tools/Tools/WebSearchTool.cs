using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Web search via the Brave Search API.</summary>
public sealed class WebSearchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IToolExecutor _executor;

    public string Name => "web_search";
    public string Description => "Performs a web search via the Brave Search API. Be specific — include asset names, dates, and key terms.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Search the web via the Brave Search API. Returns a ranked list of results suitable for identifying sources to fetch. Do NOT use this to get crypto prices — use `get_price` instead.",
        Parameters:
        [
            new("query",   "string",  Required: true,  Default: null,    "The search query. Be specific. Include asset names, dates, and key terms rather than broad keywords."),
            new("count",   "integer", Required: false, Default: "10",    "Number of results to return. Range: 1–20."),
            new("recency", "string",  Required: false, Default: "\"any\"", "Filter results by age. Accepts: \"day\", \"week\", \"month\", \"year\", \"any\"."),
        ],
        Returns: "A list of up to `count` results, each containing: `title` (page title), `url` (full URL), `description` (1–2 sentence excerpt).",
        Examples:
        [
            "{\"query\": \"Kraken exchange withdrawal issues February 2026\", \"count\": 5, \"recency\": \"week\"}",
            "{\"query\": \"Fed funds rate decision March 2026\", \"recency\": \"day\"}",
        ],
        Notes:
        [
            "Results contain snippets only — use `web_fetch` to read content in full.",
            "Prefer specific queries over broad ones. `\"Fed funds rate decision March 2026\"` returns better results than `\"Fed interest rates\"`.",
            "Use `recency` aggressively. For news and sentiment signals, stale results are worse than no results.",
            "Never use this to retrieve a crypto spot price. Use `get_price` instead.",
        ]);

    public WebSearchTool(HttpClient http, string apiKey, IToolExecutor executor)
    {
        _http = http;
        _apiKey = apiKey;
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return Task.FromResult("Error: 'query' is required.");

        var count = parameters.TryGetValue("count", out var countStr) && int.TryParse(countStr, out var c) ? c : 10;
        var recency = parameters.TryGetValue("recency", out var r) && !string.IsNullOrWhiteSpace(r) ? r : "any";

        return ExecuteCoreAsync(query, count, recency, ct);
    }

    [Description("Searches the web for current information about a topic. Returns titles, URLs, and descriptions.")]
    internal async Task<string> ExecuteCoreAsync(
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

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(WebSearchTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<QUERY>")]
            public string Query { get; init; } = string.Empty;

            [CommandOption("--count <COUNT>")]
            [DefaultValue(10)]
            public int Count { get; init; } = 10;

            [CommandOption("--recency <RECENCY>")]
            [DefaultValue("any")]
            public string Recency { get; init; } = "any";
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Query, settings.Count, settings.Recency);
            Console.WriteLine(result);
            return 0;
        }
    }
}
