using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches and extracts readable content from a web page URL.</summary>
public sealed class WebFetchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly IToolExecutor _executor;

    public string Name => "web_fetch";
    public string Description => "Fetches and extracts readable content from a web page URL.";

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Fetch and extract the readable content of a web page. Strips navigation, ads, and boilerplate, returning clean prose or markdown.",
        Parameters:
        [
            new("url",    "string", Required: true,  Default: null,          "The full URL to fetch. Must begin with `https://`."),
            new("format", "string", Required: false, Default: "\"markdown\"", "Output format: `\"markdown\"` (default) or `\"text\"`."),
        ],
        Returns: "A JSON object with `url`, `content` (cleaned readable text), and `format`. If the page is behind a paywall or content cannot be extracted, content will be empty or truncated with a `[content truncated]` marker.",
        Examples:
        [
            "{\"url\": \"https://www.federalreserve.gov/monetarypolicy/fomcminutes20260129.htm\"}",
            "{\"url\": \"https://coindesk.com/markets/2026/03/28/btc-etf-flows\", \"format\": \"text\"}",
        ],
        Notes:
        [
            "Always validate the URL from a `web_search` result before fetching. Snippets can mislead.",
            "For data-heavy pages, fetch the specific URL for the asset or topic rather than the homepage.",
            "Content is truncated at 8 000 characters. Increase `max_length` is not supported — use a more specific URL instead.",
        ]);

    public WebFetchTool(HttpClient http, IToolExecutor executor)
    {
        _http = http;
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return Task.FromResult("Error: 'url' is required.");

        var format = parameters.TryGetValue("format", out var f) && !string.IsNullOrWhiteSpace(f) ? f : "markdown";

        return ExecuteCoreAsync(url, format, ct);
    }

    [Description("Fetches a URL and returns its readable text content.")]
    internal async Task<string> ExecuteCoreAsync(
        [Description("The URL to fetch.")] string url,
        [Description("Output format: markdown or text.")] string format = "markdown",
        CancellationToken cancellationToken = default)
    {
        return await _executor.ExecuteWithResilienceAsync(Name, async ct =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Crypton/1.0)");

            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var text = ExtractText(html);

            return System.Text.Json.JsonSerializer.Serialize(new { url, content = text, format });
        }, cancellationToken);
    }

    private static string ExtractText(string html)
    {
        // Remove script/style/nav/header/footer blocks
        html = Regex.Replace(html, @"<(script|style|nav|header|footer)[^>]*>.*?</\1>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Strip remaining tags
        html = Regex.Replace(html, @"<[^>]+>", " ");

        // Normalise whitespace
        html = Regex.Replace(html, @"\s+", " ").Trim();

        const int maxLength = 8000;
        if (html.Length > maxLength)
            html = html[..maxLength] + " [content truncated]";

        return html;
    }

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(WebFetchTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<URL>")]
            public string Url { get; init; } = string.Empty;

            [CommandOption("--format <FORMAT>")]
            [DefaultValue("markdown")]
            public string Format { get; init; } = "markdown";
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.Url, settings.Format);
            Console.WriteLine(result);
            return 0;
        }
    }
}
