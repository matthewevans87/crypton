using System.ComponentModel;
using System.Text.RegularExpressions;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Fetches and extracts readable content from a web page URL.</summary>
public sealed class WebFetchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly IToolExecutor _executor;

    public string Name => "web_fetch";
    public string Description => "Fetches and extracts readable content from a web page URL.";

    public WebFetchTool(HttpClient http, IToolExecutor executor)
    {
        _http = http;
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Fetches a URL and returns its readable text content.")]
    private async Task<string> ExecuteAsync(
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
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Convert heading/paragraph elements to text
        html = Regex.Replace(html, @"<h1[^>]*>(.*?)</h1>", "\n# $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<h2[^>]*>(.*?)</h2>", "\n## $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<h3[^>]*>(.*?)</h3>", "\n### $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<p[^>]*>(.*?)</p>", "\n$1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", "");
        html = System.Net.WebUtility.HtmlDecode(html);

        // Collapse excess whitespace
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        html = Regex.Replace(html, @"[ \t]+", " ");

        if (html.Length > 8000)
            html = html[..8000] + "\n[content truncated]";

        return html.Trim();
    }
}
