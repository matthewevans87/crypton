using System.Net.Http;
using System.Text.RegularExpressions;

namespace AgentRunner.Tools;

public class WebFetchTool : Tool
{
    private readonly HttpClient _httpClient;

    public override string Name => "web_fetch";
    public override string Description => "Fetches and extracts readable content from a web page";
    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["url"] = new ToolParameterProperty { Type = "string", Description = "The URL to fetch" },
            ["format"] = new ToolParameterProperty { Type = "string", Description = "Output format: 'markdown' or 'text'", Default = "markdown" }
        },
        Required = new List<string> { "url" }
    };

    public WebFetchTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var url = parameters.GetString("url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ToolResult { Success = false, Error = "Missing or invalid 'url' parameter" };
        }

        var format = parameters.GetString("format") ?? "markdown";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Crypton/1.0)");
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return new ToolResult { Success = false, Error = $"HTTP error: {response.StatusCode}" };
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var content = ExtractContent(html, format);

            return new ToolResult 
            { 
                Success = true, 
                Data = new { url, content, format } 
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }

    private string ExtractContent(string html, string format)
    {
        var text = Regex.Replace(html, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<header[^>]*>.*?</header>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<footer[^>]*>.*?</footer>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<nav[^>]*>.*?</nav>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        text = Regex.Replace(text, @"<h1[^>]*>(.*?)</h1>", "\n# $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h2[^>]*>(.*?)</h2>", "\n## $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h3[^>]*>(.*?)</h3>", "\n### $1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<p[^>]*>(.*?)</p>", "\n$1\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        
        return text.Trim();
    }
}
