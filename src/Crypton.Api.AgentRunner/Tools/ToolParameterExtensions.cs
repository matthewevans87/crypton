using System.Text.Json;

namespace AgentRunner.Tools;

/// <summary>
/// Helpers for extracting typed values from tool parameter dictionaries.
/// Parameters arrive as Dictionary&lt;string, object&gt; where values may be
/// JsonElement (from JSON deserialization) or the .NET primitive type directly.
/// </summary>
public static class ToolParameterExtensions
{
    /// <summary>
    /// Gets a string parameter value, handling both string and JsonElement sources.
    /// Returns null if the key is missing or the value cannot be represented as a string.
    /// </summary>
    public static string? GetString(this Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value)) return null;

        return value switch
        {
            string s                                    => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je                              => je.ToString(),
            null                                        => null,
            _                                           => value.ToString()
        };
    }

    /// <summary>
    /// Gets an integer parameter value. Returns the default if missing or unconvertible.
    /// </summary>
    public static int GetInt(this Dictionary<string, object> parameters, string key, int defaultValue = 0)
    {
        if (!parameters.TryGetValue(key, out var value)) return defaultValue;

        return value switch
        {
            int i                                              => i,
            long l                                             => (int)l,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.TryGetInt32(out var n) ? n : defaultValue,
            JsonElement je                                     => int.TryParse(je.ToString(), out var n) ? n : defaultValue,
            _                                                  => int.TryParse(value?.ToString(), out var n) ? n : defaultValue
        };
    }

    /// <summary>
    /// Gets a boolean parameter value. Returns the default if missing or unconvertible.
    /// </summary>
    public static bool GetBool(this Dictionary<string, object> parameters, string key, bool defaultValue = false)
    {
        if (!parameters.TryGetValue(key, out var value)) return defaultValue;

        return value switch
        {
            bool b                                             => b,
            JsonElement { ValueKind: JsonValueKind.True }  _  => true,
            JsonElement { ValueKind: JsonValueKind.False } _  => false,
            JsonElement je                                     => bool.TryParse(je.ToString(), out var b) ? b : defaultValue,
            _                                                  => bool.TryParse(value?.ToString(), out var b) ? b : defaultValue
        };
    }
}
