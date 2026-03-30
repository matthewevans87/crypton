using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Searches the web for current information via the Brave Search API.</summary>
[Description("Search the web for current news, events, and information. Returns titles, URLs, and descriptions. Use web_fetch to read a result in full.")]
public sealed class WebSearchTool : AsyncCommand<WebSearchTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly IAnsiConsole _console;

    public WebSearchTool(HttpClient http, string apiKey, IAnsiConsole? console = null)
    {
        _http = http;
        _apiKey = apiKey;
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--query <QUERY>")]
        [Description("The search query. Include asset names, dates, and key terms.")]
        public required string Query { get; init; }

        [CommandOption("--count <COUNT>")]
        [Description("Number of results (1-20).")]
        [DefaultValue(10)]
        public int Count { get; init; } = 10;

        [CommandOption("--recency <RECENCY>")]
        [Description("Age filter: day, week, month, year, any.")]
        [DefaultValue("any")]
        public string Recency { get; init; } = "any";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _console.WriteLine("Error: Brave Search API key not configured (AGENTRUNNER__TOOLS__BRAVESEARCH__APIKEY).");
            return 1;
        }

        try
        {
            var count = Math.Clamp(settings.Count, 1, 20);
            var freshness = settings.Recency switch
            {
                "day"   => "pd",
                "week"  => "pw",
                "month" => "pm",
                "year"  => "py",
                _       => null
            };

            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(settings.Query)}&count={count}";
            if (freshness is not null)
                url += $"&freshness={freshness}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", _apiKey);

            var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(body);

            var results = new List<object>();
            if (doc.RootElement.TryGetProperty("web", out var web) &&
                web.TryGetProperty("results", out var items))
            {
                foreach (var item in items.EnumerateArray().Take(count))
                {
                    results.Add(new
                    {
                        title = item.TryGetProperty("title", out var t) ? t.GetString() : "",
                        url = item.TryGetProperty("url", out var u) ? u.GetString() : "",
                        description = item.TryGetProperty("description", out var d) ? d.GetString() : "",
                    });
                }
            }

            _console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = false }));
            return 0;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
