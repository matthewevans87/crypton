using System.Diagnostics;
using System.Text.Json;

namespace AgentRunner.Tools;

public class BirdTool : Tool
{
    private readonly int _defaultTimeoutSeconds;

    public BirdTool(int defaultTimeoutSeconds = 30)
    {
        _defaultTimeoutSeconds = defaultTimeoutSeconds;
    }

    public override string Name => "bird";

    public override string Description => "Search Twitter/X or get timeline. Modes: timeline (home feed), search (search tweets). Use --json for structured output.";

    public override ToolParameterSchema Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["mode"] = new ToolParameterProperty { Type = "string", Description = "Operation mode: timeline or search" },
            ["query"] = new ToolParameterProperty { Type = "string", Description = "Search query (required for search mode)" },
            ["limit"] = new ToolParameterProperty { Type = "integer", Description = "Number of tweets to fetch (default: 10)" }
        },
        Required = new List<string> { "mode" }
    };

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var mode = parameters.GetValueOrDefault("mode")?.ToString() ?? "timeline";
            var query = parameters.GetValueOrDefault("query")?.ToString();
            var limit = parameters.GetValueOrDefault("limit")?.ToString() ?? "10";

            if (mode == "search" && string.IsNullOrEmpty(query))
            {
                return new ToolResult
                {
                    Success = false,
                    Error = "query is required for search mode"
                };
            }

            var args = mode switch
            {
                "search" => $"search --json --count {limit} {EscapeArgument(query!)}",
                "timeline" => $"home --json --count {limit}",
                _ => $"home --json --count {limit}"
            };

            var result = await ExecuteBirdAsync(args, cancellationToken);
            stopwatch.Stop();

            return new ToolResult
            {
                Success = result.Success,
                Data = result.Output,
                Error = result.Error,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ToolResult
            {
                Success = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<(bool Success, string? Output, string? Error)> ExecuteBirdAsync(string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bird",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            var completed = await Task.Run(() => process.WaitForExit(TimeSpan.FromSeconds(_defaultTimeoutSeconds)), cancellationToken);

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return (false, null, "Bird command timed out");
            }

            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return (false, null, string.IsNullOrEmpty(error) ? $"Bird exited with code {process.ExitCode}" : error.Trim());
            }

            var output = await outputTask;

            if (string.IsNullOrWhiteSpace(output))
            {
                return (true, "[]", null);
            }

            try
            {
                var normalized = NormalizeTweetOutput(output);
                return (true, normalized, null);
            }
            catch
            {
                return (true, output, null);
            }
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private string NormalizeTweetOutput(string jsonOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var tweets = new List<object>();
                foreach (var item in root.EnumerateArray())
                {
                    var tweet = ExtractRelevantTweetData(item);
                    if (tweet != null)
                    {
                        tweets.Add(tweet);
                    }
                }
                return JsonSerializer.Serialize(tweets, new JsonSerializerOptions { WriteIndented = false });
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Array)
                {
                    var tweets = new List<object>();
                    foreach (var item in data.EnumerateArray())
                    {
                        var tweet = ExtractRelevantTweetData(item);
                        if (tweet != null)
                        {
                            tweets.Add(tweet);
                        }
                    }
                    return JsonSerializer.Serialize(tweets, new JsonSerializerOptions { WriteIndented = false });
                }
            }

            return jsonOutput;
        }
        catch
        {
            return jsonOutput;
        }
    }

    private object? ExtractRelevantTweetData(JsonElement element)
    {
        try
        {
            var result = new Dictionary<string, object?>();

            if (element.TryGetProperty("id", out var id))
            {
                result["id"] = id.ToString();
            }

            if (element.TryGetProperty("text", out var text))
            {
                result["text"] = text.GetString() ?? "";
            }

            if (element.TryGetProperty("author_id", out var authorId))
            {
                result["author_id"] = authorId.ToString();
            }

            if (element.TryGetProperty("username", out var username))
            {
                result["username"] = username.GetString();
            }

            if (element.TryGetProperty("name", out var name))
            {
                result["name"] = name.GetString();
            }

            if (element.TryGetProperty("created_at", out var createdAt))
            {
                result["created_at"] = createdAt.GetString();
            }

            if (element.TryGetProperty("public_metrics", out var metrics))
            {
                var metricsDict = new Dictionary<string, long?>();
                if (metrics.TryGetProperty("retweet_count", out var rt))
                    metricsDict["retweets"] = rt.GetInt64();
                if (metrics.TryGetProperty("like_count", out var likes))
                    metricsDict["likes"] = likes.GetInt64();
                if (metrics.TryGetProperty("reply_count", out var replies))
                    metricsDict["replies"] = replies.GetInt64();
                result["metrics"] = metricsDict;
            }

            if (result.Count == 0)
            {
                return null;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeArgument(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"'))
        {
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        }
        return arg;
    }
}
