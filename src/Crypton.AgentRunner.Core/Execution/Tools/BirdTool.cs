using System.ComponentModel;
using System.Text.Json;
using AgentRunner.Abstractions;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Search Twitter/X or get a user's timeline via the Bird server.</summary>
public sealed class BirdTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly IToolExecutor _executor;

    public string Name => "bird";
    public string Description => "Search Twitter/X or get a user's timeline. Modes: search (default), timeline.";

    public BirdTool(HttpClient http, string baseUrl, IToolExecutor executor)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _executor = executor;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Search Twitter/X posts or fetch a user's timeline.")]
    private async Task<string> ExecuteAsync(
        [Description("Search query, or @username for timeline mode.")] string query,
        [Description("Mode: search (default) or timeline.")] string mode = "search",
        [Description("Number of posts to return (1–50).")] int count = 20,
        CancellationToken cancellationToken = default)
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

            var payload = JsonSerializer.Serialize(new { args });
            var response = await _http.PostAsync(
                $"{_baseUrl}/execute",
                new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return $"Error: Bird server returned {response.StatusCode}: {body}";

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("exitCode", out var ec) && ec.GetInt32() != 0)
            {
                var stderr = doc.RootElement.TryGetProperty("stderr", out var s) ? s.GetString() : "";
                return $"Error: bird exited with code {ec.GetInt32()}: {stderr}";
            }

            return doc.RootElement.TryGetProperty("stdout", out var stdout)
                ? stdout.GetString() ?? ""
                : body;
        }, cancellationToken);
    }

    private static string EscapeArg(string arg)
    {
        arg = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{arg}\"";
    }
}
