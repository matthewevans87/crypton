namespace AgentRunner.Tools;

public abstract class Tool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ToolParameterSchema? Parameters { get; }

    public abstract Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default);

    public object ToOpenAIFunction()
    {
        var properties = new Dictionary<string, object>();
        
        if (Parameters?.Properties != null)
        {
            foreach (var (key, prop) in Parameters.Properties)
            {
                var propDict = new Dictionary<string, object>
                {
                    ["type"] = prop.Type
                };
                if (!string.IsNullOrEmpty(prop.Description))
                {
                    propDict["description"] = prop.Description;
                }
                if (prop.Default != null)
                {
                    propDict["default"] = prop.Default;
                }
                properties[key] = propDict;
            }
        }

        var function = new Dictionary<string, object>
        {
            ["description"] = Description,
            ["name"] = Name,
            ["parameters"] = new Dictionary<string, object>
            {
                ["type"] = Parameters?.Type ?? "object",
                ["properties"] = properties,
                ["required"] = Parameters?.Required ?? new List<string>()
            }
        };

        return function;
    }
}

public class ToolResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CallId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
    public TimeSpan Duration { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime CalledAt { get; set; } = DateTime.UtcNow;
}

public class ToolParameterSchema
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class ToolParameterProperty
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public object? Default { get; set; }
}
