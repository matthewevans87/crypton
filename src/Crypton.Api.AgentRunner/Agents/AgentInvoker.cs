using System.Text;
using System.Text.Json;
using AgentRunner.Configuration;
using AgentRunner.Tools;

namespace AgentRunner.Agents;

public class AgentInvoker
{
    private readonly AgentRunnerConfig _config;
    private readonly ToolExecutor _toolExecutor;
    private readonly HttpClient _httpClient;

    public AgentInvoker(AgentRunnerConfig config, ToolExecutor toolExecutor)
    {
        _config = config;
        _toolExecutor = toolExecutor;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(config.Ollama.TimeoutSeconds, 30))
        };
    }

    /// <summary>
    /// Invokes the agent for the given context.
    /// </summary>
    /// <param name="onToken">Called with each streaming token as it arrives from Ollama (null = no streaming).</param>
    /// <param name="onEvent">Called with structured event messages (tool calls, iteration markers, errors).</param>
    public async Task<AgentInvocationResult> InvokeAsync(
        AgentContext context,
        CancellationToken cancellationToken = default,
        Action<string>? onToken = null,
        Action<string>? onEvent = null)
    {
        var timeout = GetTimeoutForAgent(context.AgentName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            // Split into system message (identity + tool guide) and user message (task + BEGIN trigger).
            // This ensures the model treats its identity as stable role context, not a document to summarize.
            var systemPrompt = context.ToSystemPrompt();
            var userPrompt = context.ToUserPrompt();

            var conversationHistory = new List<ConversationMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user",   Content = userPrompt }
            };

            var allToolCalls = new List<ToolCall>();
            var allToolResults = new List<ToolResult>();

            var maxIterations = GetMaxIterationsForAgent(context.AgentName);
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                onEvent?.Invoke($"[iter {iteration + 1}/{maxIterations}] Calling LLM...");

                var output = await CallLlmAsync(
                    conversationHistory,
                    context.AgentName,
                    cts.Token,
                    onToken,
                    onEvent);

                conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = output });

                var toolCalls = ParseToolCalls(output);

                if (!toolCalls.Any())
                {
                    onEvent?.Invoke($"[iter {iteration + 1}] No tool calls — final response.");
                    return new AgentInvocationResult
                    {
                        Success = true,
                        Output = output,
                        ToolCalls = allToolCalls,
                        ToolResults = allToolResults,
                        Iterations = iteration + 1
                    };
                }

                onEvent?.Invoke($"[iter {iteration + 1}] {toolCalls.Count} tool call(s) detected.");
                allToolCalls.AddRange(toolCalls);

                var executedTools = await ExecuteToolsAsync(toolCalls, cts.Token, onEvent);
                allToolResults.AddRange(executedTools);

                foreach (var call in toolCalls)
                {
                    var result = executedTools.First(r => r.CallId == call.Id);
                    var resultContent = result.Success
                        ? JsonSerializer.Serialize(result.Data)
                        : $"Error: {result.Error}";

                    conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = $"Tool '{call.ToolName}' result: {resultContent}"
                    });
                }
            }

            var finalOutput = conversationHistory.LastOrDefault(m => m.Role == "assistant")?.Content ?? "";
            onEvent?.Invoke($"[max iterations reached ({maxIterations})] Returning last assistant response.");

            return new AgentInvocationResult
            {
                Success = true,
                Output = finalOutput,
                ToolCalls = allToolCalls,
                ToolResults = allToolResults,
                Iterations = maxIterations
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentInvocationResult
            {
                Success = false,
                Error = "Agent execution timed out"
            };
        }
        catch (Exception ex)
        {
            return new AgentInvocationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private int GetMaxIterationsForAgent(string agentName)
    {
        return agentName.ToLower() switch
        {
            "plan"       => _config.Agents.Plan.MaxIterations,
            "research"   => _config.Agents.Research.MaxIterations,
            "analysis"   => _config.Agents.Analyze.MaxIterations,
            "synthesis"  => _config.Agents.Synthesis.MaxIterations,
            "evaluation" => _config.Agents.Evaluation.MaxIterations,
            _            => _config.Agents.Plan.MaxIterations
        };
    }

    private TimeSpan GetTimeoutForAgent(string agentName)
    {
        var settings = agentName.ToLower() switch
        {
            "plan" => _config.Agents.Plan,
            "research" => _config.Agents.Research,
            "analysis" => _config.Agents.Analyze,
            "synthesis" => _config.Agents.Synthesis,
            "evaluation" => _config.Agents.Evaluation,
            _ => _config.Agents.Plan
        };

        if (_config.Cycle.StepTimeoutOverrides.TryGetValue(agentName.ToLower(), out var overrideMinutes))
        {
            return TimeSpan.FromMinutes(overrideMinutes);
        }

        return TimeSpan.FromMinutes(settings.TimeoutMinutes);
    }

    private async Task<string> CallLlmAsync(
        List<ConversationMessage> conversation,
        string agentName,
        CancellationToken cancellationToken,
        Action<string>? onToken = null,
        Action<string>? onEvent = null)
    {
        var settings = agentName.ToLower() switch
        {
            "plan" => _config.Agents.Plan,
            "research" => _config.Agents.Research,
            "analysis" => _config.Agents.Analyze,
            "synthesis" => _config.Agents.Synthesis,
            "evaluation" => _config.Agents.Evaluation,
            _ => _config.Agents.Plan
        };

        var messages = conversation.Select(m => new { role = m.Role, content = m.Content }).ToArray();
        bool streaming = onToken != null;

        var requestBody = new
        {
            model = settings.Model,
            messages,
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens,
            stream = streaming
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        var ollamaUrl = _config.Ollama.BaseUrl.TrimEnd('/') + "/api/chat";

        if (!streaming)
        {
            var response = await _httpClient.PostAsync(ollamaUrl, httpContent, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Ollama API error ({ollamaUrl}): {response.StatusCode} - {responseJson}");

            if (string.IsNullOrWhiteSpace(responseJson))
                throw new Exception("Ollama returned empty response");

            var obj = JsonSerializer.Deserialize<JsonElement>(responseJson);
            return obj.GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        // Streaming mode: read NDJSON, call onToken per chunk
        using var request = new HttpRequestMessage(HttpMethod.Post, ollamaUrl)
        {
            Content = httpContent
        };
        using var streamResponse = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!streamResponse.IsSuccessStatusCode)
        {
            var errBody = await streamResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Ollama API error ({ollamaUrl}): {streamResponse.StatusCode} - {errBody}");
        }

        await using var stream = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var fullResponse = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break; // end of stream
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch
            {
                continue;
            }

            if (chunk.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentToken))
            {
                var token = contentToken.GetString() ?? "";
                if (!string.IsNullOrEmpty(token))
                {
                    fullResponse.Append(token);
                    onToken?.Invoke(token);
                }
            }

            if (chunk.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                if (chunk.TryGetProperty("eval_count", out var evalCount) &&
                    chunk.TryGetProperty("eval_duration", out var evalDur))
                {
                    var tokensPerSec = evalCount.GetDouble() / (evalDur.GetDouble() / 1e9);
                    onEvent?.Invoke($"\n[LLM] {evalCount} tokens @ {tokensPerSec:F1} tok/s");
                }
                break;
            }
        }

        return fullResponse.ToString();
    }

    private List<ToolCall> ParseToolCalls(string output)
    {
        var calls = new List<ToolCall>();
        var seen = new HashSet<int>(); // track match start positions to avoid duplicates

        // Primary: strict pattern with closing tag
        var strictPattern = new System.Text.RegularExpressions.Regex(
            @"<tool_call>\s*(\w+)\s*(.*?)</tool_call>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in strictPattern.Matches(output))
        {
            if (seen.Add(match.Index))
                TryAddToolCall(calls, match.Groups[1].Value, match.Groups[2].Value.Trim());
        }

        // Fallback: unclosed tag — look for <tool_call>toolname {...} without </tool_call>
        // Handles the common case where the model omits the closing tag.
        var openPattern = new System.Text.RegularExpressions.Regex(
            @"<tool_call>\s*(\w+)\s*(\{[^}]*\})",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in openPattern.Matches(output))
        {
            // Only count this if the strict pattern didn't already capture it
            if (seen.Add(match.Index))
                TryAddToolCall(calls, match.Groups[1].Value, match.Groups[2].Value.Trim());
        }

        return calls;
    }

    private static void TryAddToolCall(List<ToolCall> calls, string toolName, string paramsJson)
    {
        Dictionary<string, object> parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsJson)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            parameters = new Dictionary<string, object>();
        }

        calls.Add(new ToolCall
        {
            ToolName = toolName,
            Parameters = parameters
        });
    }

    private async Task<List<ToolResult>> ExecuteToolsAsync(
        List<ToolCall> calls,
        CancellationToken cancellationToken,
        Action<string>? onEvent = null)
    {
        var results = new List<ToolResult>();

        foreach (var call in calls)
        {
            onEvent?.Invoke($"[tool] → {call.ToolName}({JsonSerializer.Serialize(call.Parameters)})");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _toolExecutor.ExecuteAsync(call, cancellationToken);
            sw.Stop();

            if (result.Success)
            {
                var preview = JsonSerializer.Serialize(result.Data);
                var truncated = preview.Length > 200 ? preview[..200] + "..." : preview;
                onEvent?.Invoke($"[tool] ← {call.ToolName} OK ({sw.Elapsed.TotalSeconds:F1}s): {truncated}");
            }
            else
            {
                onEvent?.Invoke($"[tool] ← {call.ToolName} FAILED ({sw.Elapsed.TotalSeconds:F1}s): {result.Error}");
            }

            results.Add(result);
        }

        return results;
    }
}

public class AgentInvocationResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public List<ToolCall> ToolCalls { get; set; } = new();
    public List<ToolResult> ToolResults { get; set; } = new();
    public int Iterations { get; set; }
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
