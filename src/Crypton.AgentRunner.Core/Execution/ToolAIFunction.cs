using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Crypton.Tools;
using Crypton.Tools.Infrastructure;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace AgentRunner.Execution;

/// <summary>
/// Bridges a <see cref="ToolRegistration"/> to <see cref="AIFunction"/> so that
/// registered CLI tools can be passed to <see cref="ChatOptions.Tools"/> and
/// their schemas surfaced to the model via native Ollama tool calling.
/// <para>
/// Tool execution is intentionally left to <see cref="LlmAgentExecutor"/>, which
/// handles parallel dispatch and event publishing.  This class is used for
/// schema exposition only; <see cref="InvokeCoreAsync"/> delegates to the virtual
/// terminal for the rare cases where a caller invokes it directly.
/// </para>
/// </summary>
internal sealed class ToolAIFunction : AIFunction
{
    private readonly AIFunctionMetadata _metadata;
    private readonly IVirtualTerminal _terminal;
    private readonly string _name;

    public ToolAIFunction(ToolRegistration reg, IVirtualTerminal terminal)
    {
        _terminal = terminal;
        _name = reg.Name;
        _metadata = new AIFunctionMetadata(reg.Name)
        {
            Description = reg.Description,
            Parameters = BuildParameters(reg.SettingsType),
        };
    }

    public override AIFunctionMetadata Metadata => _metadata;

    protected override async Task<object?> InvokeCoreAsync(
        IEnumerable<KeyValuePair<string, object?>> arguments,
        CancellationToken cancellationToken)
    {
        var argsJson = JsonSerializer.Serialize(
            arguments.ToDictionary(k => k.Key, v => v.Value));
        var result = await _terminal.RunAsync(_name, argsJson, cancellationToken);
        return result.Output;
    }

    // ─── Schema helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<AIFunctionParameterMetadata> BuildParameters(Type settingsType)
    {
        var result = new List<AIFunctionParameterMetadata>();

        foreach (var prop in settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var option = prop.GetCustomAttribute<CommandOptionAttribute>();
            if (option is null) continue;

            var name = ExtractPropertyName(option.LongNames);
            if (name is null) continue;

            var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var defaultAttr = prop.GetCustomAttribute<DefaultValueAttribute>();
            var isOptional = IsOptionalType(prop.PropertyType) || defaultAttr is not null;

            result.Add(new AIFunctionParameterMetadata(name)
            {
                Description = description,
                IsRequired = !isOptional,
                HasDefaultValue = defaultAttr is not null,
                DefaultValue = defaultAttr?.Value,
                ParameterType = prop.PropertyType,
                Schema = BuildTypeSchema(prop.PropertyType),
            });
        }

        return result;
    }

    private static JsonElement BuildTypeSchema(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var typeName = underlying switch
        {
            _ when underlying == typeof(int) || underlying == typeof(long) => "integer",
            _ when underlying == typeof(bool) => "boolean",
            _ when underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal) => "number",
            _ => "string",
        };
        return JsonDocument.Parse($"{{\"type\":\"{typeName}\"}}").RootElement.Clone();
    }

    /// <summary>Returns the long name without dashes (the schema property key).</summary>
    private static string? ExtractPropertyName(IReadOnlyList<string> longNames)
    {
        foreach (var n in longNames) return n;
        return null;
    }

    private static bool IsOptionalType(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null || type == typeof(bool);
}
