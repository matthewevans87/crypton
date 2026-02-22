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
            var output = await CallLlmAsync(prompt, context.AgentName, cts.Token);
            
            var toolCalls = ParseToolCalls(output);
            
            if (!toolCalls.Any())
            {
                return new AgentInvocationResult
                {
                    Success = true,
                    Output = output,
                    ToolCalls = new List<ToolCall>()
                };
            }

            var executedTools = await ExecuteToolsWithRetryAsync(toolCalls, cts.Token);
            
            var finalOutput = await ContinueConversationAsync(
                prompt, 
                output, 
                executedTools, 
                context.AgentName, 
                cts.Token);

            return new AgentInvocationResult
            {
                Success = true,
                Output = finalOutput,
                ToolCalls = toolCalls,
                ToolResults = executedTools
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

        return TimeSpan.FromMinutes(settings.TimeoutMinutes);
    }

    private async Task<string> CallLlmAsync(string prompt, string agentName, CancellationToken cancellationToken)
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

        var requestBody = new
        {
            model = settings.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = settings.Temperature,
            max_tokens = settings.MaxTokens
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = new HttpClient();
        var response = await client.PostAsync(
            "http://localhost:11434/api/generate",
            content,
            cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
        
        return responseObj.GetProperty("response").GetString() ?? "";
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
}
