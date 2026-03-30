using Spectre.Console.Cli;

namespace Crypton.Tools;

/// <summary>
/// A registration entry for a single tool command.
/// </summary>
/// <param name="Name">The canonical tool name (e.g. <c>"web_search"</c>).</param>
/// <param name="Description">One-line description surfaced to the LLM in Phase 1 tool selection.</param>
/// <param name="SettingsType">The <see cref="CommandSettings"/> subtype for this command.
/// Used by <see cref="Infrastructure.ToolSchemaGenerator"/> to derive the JSON schema.</param>
/// <param name="AppFactory">Creates a ready-to-run <see cref="ICommandApp"/> for this tool,
/// wired to write output to the provided <see cref="Spectre.Console.IAnsiConsole"/>.</param>
public sealed record ToolRegistration(
    string Name,
    string Description,
    Type SettingsType,
    Func<Spectre.Console.IAnsiConsole, ICommandApp> AppFactory);

/// <summary>
/// Provides the set of registered tool commands and their metadata.
/// Consumed by <see cref="IVirtualTerminal"/> (for invocation) and by the agent context
/// builder (for Phase 1 tool-list prompt and Phase 2 schema injection).
/// </summary>
public interface IToolRegistry
{
    /// <summary>Returns name + one-line description for every registered tool.</summary>
    IReadOnlyList<(string Name, string Description)> GetAllDescriptions();

    /// <summary>Returns name + description for the subset of tools in <paramref name="toolNames"/>.</summary>
    IReadOnlyList<(string Name, string Description)> GetDescriptions(IReadOnlyList<string> toolNames);

    /// <summary>Returns the JSON Schema string for the specified tool's settings.</summary>
    /// <exception cref="KeyNotFoundException">Tool not registered.</exception>
    string GetJsonSchema(string toolName);

    /// <summary>Returns the full registration or <see langword="false"/> if not found.</summary>
    bool TryGetRegistration(string toolName, out ToolRegistration registration);
}
