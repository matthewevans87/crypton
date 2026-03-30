using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Searches Twitter/X or fetches a user's timeline via the Bird server.</summary>
[Description("Search Twitter/X posts or fetch a user's recent timeline for sentiment and market signals.")]
public sealed class BirdTool : AsyncCommand<BirdTool.Settings>
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly IAnsiConsole _console;

    public BirdTool(HttpClient http, string baseUrl, IAnsiConsole? console = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--query <QUERY>")]
        [Description("Search query, or @username for timeline mode.")]
        public required string Query { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Mode: search (default) or timeline.")]
        [DefaultValue("search")]
        public string Mode { get; init; } = "search";

        [CommandOption("--count <COUNT>")]
        [Description("Number of posts to return (1-50).")]
        [DefaultValue(20)]
        public int Count { get; init; } = 20;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var count = Math.Clamp(settings.Count, 1, 50);
            var query = settings.Query;
            var mode = settings.Mode;

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
            var response = await _http.PostAsJsonAsync($"{_baseUrl}/execute", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("exitCode", out var ec) && ec.GetInt32() != 0)
            {
                var stderr = doc.RootElement.TryGetProperty("stderr", out var se) ? se.GetString() : "";
                _console.WriteLine($"Error: bird exited with code {ec.GetInt32()}: {stderr}");
                return 1;
            }

            var output = doc.RootElement.TryGetProperty("stdout", out var stdout)
                ? stdout.GetString() ?? ""
                : body;

            _console.WriteLine(output);
            return 0;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string EscapeArg(string arg)
    {
        arg = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{arg}\"";
    }
}
