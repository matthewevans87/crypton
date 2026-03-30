using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches and extracts readable content from a web page URL.</summary>
[Description("Fetch and extract readable text from a web page URL. Use after web_search to read a result in full.")]
public sealed class WebFetchTool : AsyncCommand<WebFetchTool.Settings>
{
    private readonly HttpClient _http;
    private readonly IAnsiConsole _console;

    public WebFetchTool(HttpClient http, IAnsiConsole? console = null)
    {
        _http = http;
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--url <URL>")]
        [Description("The full URL to fetch. Must begin with https://.")]
        public required string Url { get; init; }

        [CommandOption("--format <FORMAT>")]
        [Description("Output format: markdown (default) or text.")]
        [DefaultValue("markdown")]
        public string Format { get; init; } = "markdown";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, settings.Url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Crypton/1.0)");

            var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = ExtractText(html);

            _console.WriteLine(JsonSerializer.Serialize(
                new { url = settings.Url, content = text, format = settings.Format },
                new JsonSerializerOptions { WriteIndented = false }));
            return 0;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string ExtractText(string html)
    {
        html = Regex.Replace(html, @"<(script|style|nav|header|footer)[^>]*>.*?</\1>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = Regex.Replace(html, @"\s+", " ").Trim();

        const int maxLength = 8000;
        if (html.Length > maxLength)
            html = html[..maxLength] + " [content truncated]";

        return html;
    }
}
