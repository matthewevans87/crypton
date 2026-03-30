using System.Text;

namespace Crypton.Tools;

/// <summary>Describes a single parameter of a callable tool.</summary>
public sealed record ToolParameter(
    string Name,
    string Type,
    bool Required,
    string? Default,
    string Description);

/// <summary>
/// Self-contained documentation for a callable tool. Each <see cref="ITool"/>
/// exposes a <c>Documentation</c> property returning one of these.
/// </summary>
public sealed record ToolDocumentation(
    string LongDescription,
    IReadOnlyList<ToolParameter> Parameters,
    string Returns,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Renders the full markdown block for this tool, suitable for inclusion in the
    /// <c># Tool Reference</c> section of an agent's system prompt.
    /// </summary>
    public string ToMarkdown(string toolName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## `{toolName}`");
        sb.AppendLine();
        sb.AppendLine(LongDescription);
        sb.AppendLine();

        if (Parameters.Count > 0)
        {
            sb.AppendLine("### Parameters");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Type | Required | Default | Description |");
            sb.AppendLine("| --------- | ---- | -------- | ------- | ----------- |");
            foreach (var p in Parameters)
            {
                var req = p.Required ? "yes" : "no";
                var def = p.Default is null ? "—" : $"`{p.Default}`";
                sb.AppendLine($"| `{p.Name}` | {p.Type} | {req} | {def} | {p.Description} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Returns");
        sb.AppendLine();
        sb.AppendLine(Returns);
        sb.AppendLine();

        if (Examples.Count > 0)
        {
            sb.AppendLine("### Example");
            sb.AppendLine();
            sb.AppendLine("```json");
            foreach (var e in Examples)
                sb.AppendLine(e);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (Notes.Count > 0)
        {
            sb.AppendLine("### Notes");
            sb.AppendLine();
            foreach (var n in Notes)
                sb.AppendLine($"- {n}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
