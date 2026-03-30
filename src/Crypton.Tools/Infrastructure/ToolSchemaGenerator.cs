using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Spectre.Console.Cli;

namespace Crypton.Tools.Infrastructure;

/// <summary>
/// Derives a JSON Schema object from a <see cref="CommandSettings"/> subtype by reflecting
/// over <see cref="CommandOptionAttribute"/>, <see cref="DescriptionAttribute"/>, and
/// <see cref="DefaultValueAttribute"/> decorations on each property.
/// <para>
/// All tool settings must use <see cref="CommandOptionAttribute"/> exclusively
/// (no positional <see cref="CommandArgumentAttribute"/>). This keeps
/// JSON → CLI args conversion trivial.
/// </para>
/// </summary>
public static class ToolSchemaGenerator
{
    /// <summary>
    /// Generates a JSON Schema (draft-07 compatible) for the given settings type.
    /// </summary>
    public static string GenerateSchema(Type settingsType)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var option = prop.GetCustomAttribute<CommandOptionAttribute>();
            if (option is null)
                continue;

            var propName = ExtractPropertyName(option.LongNames);
            if (propName is null)
                continue;

            var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var defaultAttr = prop.GetCustomAttribute<DefaultValueAttribute>();

            var schema = BuildPropertySchema(prop.PropertyType, description, defaultAttr?.Value);
            properties[propName] = schema;

            // Required = no [DefaultValue] AND not a nullable type AND not bool (bools default to false)
            if (defaultAttr is null && !IsOptionalType(prop.PropertyType))
                required.Add(propName);
        }

        var root = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
            root["required"] = required;

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Converts a JSON args object to a CLI <c>string[]</c> by mapping each JSON property
    /// to its corresponding <c>--option value</c> pair.
    /// </summary>
    public static string[] JsonToArgs(Type settingsType, string jsonArgs)
    {
        // Map lowercase property name → option long name (e.g. "query" → "--query")
        var nameMap = BuildNameMap(settingsType);

        if (string.IsNullOrWhiteSpace(jsonArgs) || jsonArgs.Trim() == "{}")
            return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonArgs);
        }
        catch
        {
            return [];
        }

        // Unwrap {"args": {...}} if caller wraps in an args envelope
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("args", out var inner) &&
            inner.ValueKind == JsonValueKind.Object)
        {
            root = inner;
        }

        var args = new List<string>();

        foreach (var prop in root.EnumerateObject())
        {
            var key = prop.Name.ToLowerInvariant();
            if (!nameMap.TryGetValue(key, out var optionName))
                continue;

            var value = prop.Value;
            if (value.ValueKind == JsonValueKind.True)
            {
                args.Add(optionName);
            }
            else if (value.ValueKind == JsonValueKind.False)
            {
                // Boolean false — omit flag entirely (Spectre default)
            }
            else if (value.ValueKind != JsonValueKind.Null)
            {
                args.Add(optionName);
                args.Add(value.ValueKind == JsonValueKind.String
                    ? value.GetString()!
                    : value.ToString());
            }
        }

        return [.. args];
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, string> BuildNameMap(Type settingsType)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var option = prop.GetCustomAttribute<CommandOptionAttribute>();
            if (option is null) continue;

            var longName = ExtractLongName(option.LongNames);
            if (longName is null) continue;

            // Allow JSON key to match either the property name or the option long name without dashes
            var keyByProp = prop.Name.ToLowerInvariant();
            var keyByOption = longName.TrimStart('-').ToLowerInvariant();

            map[keyByProp] = longName;
            map[keyByOption] = longName;
        }
        return map;
    }

    private static string? ExtractPropertyName(IReadOnlyList<string> longNames)
    {
        // LongNames are already stripped of dashes by Spectre; return as-is for schema keys
        foreach (var n in longNames)
            return n;
        return null;
    }

    private static string? ExtractLongName(IReadOnlyList<string> longNames)
    {
        // LongNames are already stripped of dashes by Spectre; re-add them for CLI args
        foreach (var n in longNames)
            return $"--{n}";
        return null;
    }

    private static Dictionary<string, object> BuildPropertySchema(
        Type type, string description, object? defaultValue)
    {
        var schema = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(description))
            schema["description"] = description;

        if (defaultValue is not null)
            schema["default"] = defaultValue;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
            schema["type"] = "string";
        else if (underlying == typeof(int) || underlying == typeof(long))
            schema["type"] = "integer";
        else if (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal))
            schema["type"] = "number";
        else if (underlying == typeof(bool))
            schema["type"] = "boolean";
        else
            schema["type"] = "string"; // fallback

        return schema;
    }

    private static bool IsOptionalType(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null || type == typeof(bool);
}
