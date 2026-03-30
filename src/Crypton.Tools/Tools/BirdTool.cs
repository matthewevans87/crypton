using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Search Twitter/X or get a user's timeline via the Bird server.</summary>
public sealed class BirdTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly IToolExecutor _executor;

    public string Name => "bird";
    public string Description => "Search Twitter/X or get a user's timeline. Modes: search (default), timeline.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Query X (Twitter) for posts, user timelines, and trending topics via the `bird` CLI. Used for real-time sentiment, influencer signals, and first-hand market commentary.",
        Parameters:
        [
            new("query",   "string",  Required: true,  Default: null,          "A search query or `@username` for a specific account's timeline. Supports standard X search operators (`from:`, `since:`, `until:`, `#hashtag`, `-filter:retweets`)."),
            new("mode",    "string",  Required: false, Default: "\"search\"",   "`\"search\"` for keyword/operator queries; `\"timeline\"` for a specific user's recent posts. When using `\"timeline\"`, `query` should be `@username`."),
            new("count",   "integer", Required: false, Default: "20",           "Number of posts to return. Range: 1–50."),
        ],
        Returns: "Raw JSON output from the bird CLI. Each post contains author, text, timestamp, and engagement counts where available.",
        Examples:
        [
            "{\"query\": \"@unusualwhales\", \"mode\": \"timeline\", \"count\": 10}",
            "{\"query\": \"bitcoin ETF inflows -filter:retweets\", \"count\": 20}",
        ],
        Notes:
        [
            "Social signals degrade quickly — default search covers the last day.",
            "Cross-verify social claims with `web_search` before including them in findings. X is a primary signal source, not a ground truth.",
            "For trend-spotting, use keyword search. For known high-signal accounts (e.g. `@unusualwhales`, `@WatcherGuru`), use `timeline` mode directly.",
        ]);

    public BirdTool(HttpClient http, string baseUrl, IToolExecutor executor)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return Task.FromResult("Error: 'query' is required.");

        var mode = parameters.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "search";
        var count = parameters.TryGetValue("count", out var countStr) && int.TryParse(countStr, out var c) ? c : 20;

        return ExecuteCoreAsync(query, mode, count, ct);
    }

    [Description("Search Twitter/X posts or fetch a user's timeline.")]
    internal async Task<string> ExecuteCoreAsync(
        [Description("Search query, or @username for timeline mode.")] string query,
        [Description("Mode: search (default) or timeline.")] string mode = "search",
        [Description("Number of posts to return (1–50).")] int count = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
            {
                count = Math.Clamp(count, 1, 50);

                var args = mode switch
                {
                    "timeline" when query.StartsWith('@') =>
                        $"search --json -n {count} {EscapeArg($"from:{query[1..]}")}",
                    "timeline" =>
                        $"home --json -n {count}",
                    _ =>
                        $"search --json -n {count} {EscapeArg(query)}"
                };

                var payload = new { args };
                var response = await _http.PostAsJsonAsync($"{_baseUrl}/execute", payload, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("exitCode", out var ec) && ec.GetInt32() != 0)
                {
                    var stderr = doc.RootElement.TryGetProperty("stderr", out var se) ? se.GetString() : "";
                    return $"Error: bird exited with code {ec.GetInt32()}: {stderr}";
                }

                return doc.RootElement.TryGetProperty("stdout", out var stdout)
                    ? stdout.GetString() ?? ""
                    : body;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string EscapeArg(string arg)
    {
        arg = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{arg}\"";
    }

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(BirdTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<QUERY>")]
            public string Query { get; init; } = string.Empty;

            [CommandOption("--mode <MODE>")]
            [DefaultValue("search")]
            public string Mode { get; init; } = "search";

            [CommandOption("--count <COUNT>")]
            [DefaultValue(20)]
            public int Count { get; init; } = 20;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Query, settings.Mode, settings.Count);
            Console.WriteLine(result);
            return 0;
        }
    }
}
