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

            // Build the Ollama-format tool definitions for this agent's allowed tools.
            var tools = _toolExecutor.GetAllTools().Values
                .Where(t => context.AvailableTools.Contains(t.Name))
                .Select(t => (object)new { type = "function", function = t.ToOpenAIFunction() })
                .ToArray();

            var maxIterations = GetMaxIterationsForAgent(context.AgentName);
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                onEvent?.Invoke($"[iter {iteration + 1}/{maxIterations}] Calling LLM...");

                var llmResult = await CallLlmAsync(
                    conversationHistory,
                    context.AgentName,
                    tools,
                    cts.Token,
                    onToken,
                    onEvent);

                // Add the assistant turn; include tool_calls when present so that
                // the follow-up conversation is well-formed for Ollama.
                var assistantMsg = new ConversationMessage { Role = "assistant", Content = llmResult.Content };
                if (llmResult.ToolCalls.Count > 0)
                    assistantMsg.ToolCalls = llmResult.ToolCalls;
                conversationHistory.Add(assistantMsg);

                var toolCalls = ConvertNativeToolCalls(llmResult.ToolCalls);

                if (!toolCalls.Any())
                {
                    onEvent?.Invoke($"[iter {iteration + 1}] No tool calls — final response.");
                    return new AgentInvocationResult
                    {
                        Success = true,
                        Output = llmResult.Content,
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

                    // role=tool is the correct Ollama format for returning function results.
                    conversationHistory.Add(new ConversationMessage
                    {
                        Role = "tool",
                        Content = resultContent
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
            "plan" => _config.Agents.Plan.MaxIterations,
            "research" => _config.Agents.Research.MaxIterations,
            "analysis" => _config.Agents.Analyze.MaxIterations,
            "synthesis" => _config.Agents.Synthesis.MaxIterations,
            "evaluation" => _config.Agents.Evaluation.MaxIterations,
            _ => _config.Agents.Plan.MaxIterations
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

    private async Task<LlmCallResult> CallLlmAsync(
        List<ConversationMessage> conversation,
        string agentName,
        object[] tools,
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

        // Serialize conversation; assistant messages may carry tool_calls from a previous turn.
        var messages = conversation.Select<ConversationMessage, object>(m =>
        {
            if (m.ToolCalls is { Count: > 0 })
            {
                return new
                {
                    role = m.Role,
                    content = m.Content,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        function = new { name = tc.Name, arguments = tc.Arguments }
                    }).ToArray()
                };
            }
            return new { role = m.Role, content = m.Content, tool_calls = (object?)null };
        }).ToArray();

        bool streaming = onToken != null;

        var requestBody = new
        {
            model = settings.Model,
            messages,
            stream = streaming,
            tools,
            options = new
            {
                num_ctx = _config.Ollama.NumCtx,
                temperature = settings.Temperature,
                // num_predict is intentionally omitted — Ollama defaults to "fill context"
                // (-2), allowing the model to generate its full response without truncation.
            }
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
            var messageEl = obj.GetProperty("message");
            var content = messageEl.TryGetProperty("content", out var ct) ? ct.GetString() ?? "" : "";
            var nativeToolCalls = ParseNativeToolCalls(messageEl);
            return new LlmCallResult(content, nativeToolCalls);
        }

        // Streaming mode: read NDJSON, call onToken per chunk, accumulate tool_calls.
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
        var streamedToolCalls = new List<OllamaNativeToolCall>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement chunk;
            try { chunk = JsonSerializer.Deserialize<JsonElement>(line); }
            catch { continue; }

            if (chunk.TryGetProperty("message", out var msg))
            {
                if (msg.TryGetProperty("content", out var contentToken))
                {
                    var token = contentToken.GetString() ?? "";
                    if (!string.IsNullOrEmpty(token))
                    {
                        fullResponse.Append(token);
                        onToken?.Invoke(token);
                    }
                }

                // Tool calls arrive in the final chunk for thinking models.
                streamedToolCalls.AddRange(ParseNativeToolCalls(msg));
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

        return new LlmCallResult(fullResponse.ToString(), streamedToolCalls);
    }

    /// <summary>Parses tool_calls from an Ollama message JSON element.</summary>
    private static List<OllamaNativeToolCall> ParseNativeToolCalls(JsonElement messageEl)
    {
        var result = new List<OllamaNativeToolCall>();

        if (!messageEl.TryGetProperty("tool_calls", out var tcEl) ||
            tcEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var tc in tcEl.EnumerateArray())
        {
            if (!tc.TryGetProperty("function", out var fn)) continue;

            var name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(name)) continue;

            var args = new Dictionary<string, JsonElement>();
            if (fn.TryGetProperty("arguments", out var argsEl))
            {
                if (argsEl.ValueKind == JsonValueKind.Object)
                {
                    args = argsEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                }
                else if (argsEl.ValueKind == JsonValueKind.String)
                {
                    // Some model/Ollama versions serialise arguments as a JSON string.
                    try
                    {
                        using var doc = JsonDocument.Parse(argsEl.GetString() ?? "{}");
                        args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                    }
                    catch { /* fall through with empty args */ }
                }
            }

            result.Add(new OllamaNativeToolCall(name, args));
        }

        return result;
    }

    /// <summary>Converts Ollama native tool calls to the internal ToolCall model.</summary>
    private static List<ToolCall> ConvertNativeToolCalls(List<OllamaNativeToolCall> nativeCalls) =>
        nativeCalls.Select(tc => new ToolCall
        {
            ToolName = tc.Name,
            Parameters = tc.Arguments.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
        }).ToList();

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
    /// <summary>Non-null when this is an assistant message that issued tool calls (native Ollama tool calling).</summary>
    public List<OllamaNativeToolCall>? ToolCalls { get; set; }
}

/// <summary>A tool call returned by Ollama via message.tool_calls.</summary>
public sealed class OllamaNativeToolCall
{
    public OllamaNativeToolCall(string name, Dictionary<string, System.Text.Json.JsonElement> arguments)
    {
        Name = name;
        Arguments = arguments;
    }
    public string Name { get; }
    public Dictionary<string, System.Text.Json.JsonElement> Arguments { get; }
}

/// <summary>Result returned by CallLlmAsync — visible content plus any structured tool calls.</summary>
internal readonly record struct LlmCallResult(
    string Content,
    List<OllamaNativeToolCall> ToolCalls);
