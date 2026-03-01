using System.Text.Json;
using AgentRunner.Configuration;
using AgentRunner.Tools;

namespace AgentRunner.Tools;

public class ToolRegistry
{
    private readonly ToolExecutor _executor;
    private readonly Dictionary<string, ToolDefinition> _definitions = new();

    public ToolRegistry(AgentRunnerConfig config)
    {
        _executor = new ToolExecutor(
            defaultTimeoutSeconds: config.Tools.DefaultTimeoutSeconds,
            maxRetries: config.Tools.MaxRetries,
            maxRetryDelaySeconds: config.Tools.MaxRetryDelaySeconds);
        InitializeTools(config);
    }

    public ToolExecutor Executor => _executor;

    private void InitializeTools(AgentRunnerConfig config)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(config.Tools.DefaultTimeoutSeconds * 2);

        var webSearchTool = new WebSearchTool(httpClient, config.Tools.BraveSearch.ApiKey);
        var webFetchTool = new WebFetchTool(httpClient);
        var currentPositionTool = new CurrentPositionTool(
            httpClient,
            config.Tools.MarketDataService.BaseUrl,
            config.Tools.CacheTtlSeconds);
        var technicalIndicatorsTool = new TechnicalIndicatorsTool(
            httpClient,
            config.Tools.MarketDataService.BaseUrl,
            config.Tools.CacheTtlSeconds);
        var birdTool = new BirdTool(config.Tools.DefaultTimeoutSeconds);

        _executor.RegisterTool(webSearchTool);
        _executor.RegisterTool(webFetchTool);
        _executor.RegisterTool(currentPositionTool);
        _executor.RegisterTool(technicalIndicatorsTool);
        _executor.RegisterTool(birdTool);

        foreach (var tool in _executor.GetAllTools().Values)
        {
            _definitions[tool.Name] = new ToolDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.Parameters
            };
        }
    }

    public IReadOnlyDictionary<string, ToolDefinition> GetAllDefinitions() => _definitions;

    public string GetToolDescriptionsJson()
    {
        return JsonSerializer.Serialize(_definitions.Values.ToList(), new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public string GetToolsOpenAIFormat()
    {
        var tools = _executor.GetAllTools().Values
            .Select(tool => tool.ToOpenAIFunction())
            .ToList();

        var wrapper = new Dictionary<string, object>
        {
            ["tools"] = tools
        };

        return JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public List<object> GetToolsAsOpenAIFunctions()
    {
        return _executor.GetAllTools().Values
            .Select(tool => tool.ToOpenAIFunction())
            .ToList();
    }
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ToolParameterSchema? Parameters { get; set; }
}
