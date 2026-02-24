using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentRunner.Agents;
using AgentRunner.Configuration;
using AgentRunner.Tools;

namespace AgentRunner.Agents;

public class AgentInvoker
{
    private readonly AgentRunnerConfig _config;
    private readonly ToolExecutor _toolExecutor;
    private readonly int _maxToolIterations = 10;

    public AgentInvoker(AgentRunnerConfig config, ToolExecutor toolExecutor)
    {
        _config = config;
        _toolExecutor = toolExecutor;
    }

    public async Task<AgentInvocationResult> InvokeAsync(
        AgentContext context, 
        CancellationToken cancellationToken = default)
    {
        var timeout = GetTimeoutForAgent(context.AgentName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var prompt = context.ToPrompt();
            var conversationHistory = new List<ConversationMessage>
            {
                new() { Role = "user", Content = prompt }
            };
            
            var allToolCalls = new List<ToolCall>();
            var allToolResults = new List<ToolResult>();
            
            for (int iteration = 0; iteration < _maxToolIterations; iteration++)
            {
                var output = await CallLlmAsync(conversationHistory, context.AgentName, cts.Token);
                
                conversationHistory.Add(new ConversationMessage { Role = "assistant", Content = output });
                
                var toolCalls = ParseToolCalls(output);
                
                if (!toolCalls.Any())
                {
                    return new AgentInvocationResult
                    {
                        Success = true,
                        Output = output,
                        ToolCalls = allToolCalls,
                        ToolResults = allToolResults,
                        Iterations = iteration + 1
                    };
                }

                allToolCalls.AddRange(toolCalls);
                
                var executedTools = await ExecuteToolsWithRetryAsync(toolCalls, cts.Token);
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
            
            return new AgentInvocationResult
            {
                Success = true,
                Output = finalOutput,
                ToolCalls = allToolCalls,
                ToolResults = allToolResults,
                Iterations = _maxToolIterations
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

        var baseTimeout = TimeSpan.FromMinutes(settings.TimeoutMinutes);
        
        if (_config.Cycle.StepTimeoutOverrides.TryGetValue(agentName.ToLower(), out var overrideMinutes))
        {
            return TimeSpan.FromMinutes(overrideMinutes);
        }

        return baseTimeout;
    }

    private async Task<string> CallLlmAsync(string prompt, string agentName, CancellationToken cancellationToken)
    {
        return await CallLlmAsync(new List<ConversationMessage> { new() { Role = "user", Content = prompt } }, agentName, cancellationToken);
    }

    private async Task<string> CallLlmAsync(List<ConversationMessage> conversation, string agentName, CancellationToken cancellationToken)
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
        
        var requestBody = new
        {
            model = settings.Model,
            messages = messages,
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = new HttpClient();
        var response = await client.PostAsync(
            "http://localhost:11434/api/chat",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        Console.WriteLine($"[DEBUG] Ollama response status: {response.StatusCode}");
        Console.WriteLine($"[DEBUG] Ollama response content ({responseJson.Length} chars): {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Ollama API error: {response.StatusCode} - {responseJson}");
        }
        
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new Exception("Ollama returned empty response");
        }
        
        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
        
        return responseObj.GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private List<ToolCall> ParseToolCalls(string output)
    {
        var calls = new List<ToolCall>();
        
        var toolPattern = new System.Text.RegularExpressions.Regex(
            @"<tool_call>\s*(\w+)\s*(.*?)</tool_call>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in toolPattern.Matches(output))
        {
            var toolName = match.Groups[1].Value;
            var paramsJson = match.Groups[2].Value;
            
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

        return calls;
    }

    private async Task<List<ToolResult>> ExecuteToolsWithRetryAsync(
        List<ToolCall> calls, 
        CancellationToken cancellationToken)
    {
        var results = new List<ToolResult>();

        foreach (var call in calls)
        {
            var result = await _toolExecutor.ExecuteAsync(call, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private async Task<string> ContinueConversationAsync(
        string originalPrompt,
        string agentOutput,
        List<ToolResult> toolResults,
        string agentName,
        CancellationToken cancellationToken)
    {
        var toolResultsJson = JsonSerializer.Serialize(toolResults.Select(r => new
        {
            success = r.Success,
            error = r.Error,
            data = r.Data
        }));

        var continuationPrompt = $@"
Previous response:
{agentOutput}

Tool execution results:
{toolResultsJson}

Please continue your analysis or provide your final output.
";

        return await CallLlmAsync(originalPrompt + "\n\n" + continuationPrompt, agentName, cancellationToken);
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
