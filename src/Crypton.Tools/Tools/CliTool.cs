using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Fetches and extracts readable content from a web page URL.</summary>
public sealed class WebFetchService
{
    private readonly HttpClient _http;
    private readonly IToolExecutor _executor;

    public WebFetchService(HttpClient http, IToolExecutor executor)
    {
        _http = http;
        _executor = executor;
    }

    public async Task<string> Execute(string url, string format = "markdown", CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Crypton/1.0)");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        var text = ExtractText(html);

        return System.Text.Json.JsonSerializer.Serialize(new { url, content = text, format });
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
}