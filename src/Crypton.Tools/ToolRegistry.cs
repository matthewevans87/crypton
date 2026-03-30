using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Crypton.Tools.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools;

/// <summary>
/// In-memory implementation of <see cref="IToolRegistry"/>. Tools are registered at startup
/// via <see cref="Register{TCommand,TSettings}"/>.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolRegistration> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a tool command. The <paramref name="appFactory"/> receives a capturing
    /// <see cref="IAnsiConsole"/> and returns a fully-configured <see cref="CommandApp"/>
    /// for in-process invocation.
    /// </summary>
    public void Register<TCommand, TSettings>(
        string name,
        Func<IAnsiConsole, ICommandApp> appFactory)
        where TCommand : class, ICommand
        where TSettings : CommandSettings
    {
        // Extract description from [Description] on the command class (CLI-native mechanism)
        var description = typeof(TCommand)
            .GetCustomAttribute<DescriptionAttribute>()
            ?.Description ?? string.Empty;

        _tools[name] = new ToolRegistration(name, description, typeof(TSettings), appFactory);
    }

    public IReadOnlyList<(string Name, string Description)> GetAllDescriptions() =>
        _tools.Values.Select(r => (r.Name, r.Description)).ToList();

    public IReadOnlyList<(string Name, string Description)> GetDescriptions(IReadOnlyList<string> toolNames) =>
        toolNames
            .Where(_tools.ContainsKey)
            .Select(n => (n, _tools[n].Description))
            .ToList();

    public string GetJsonSchema(string toolName)
    {
        if (!_tools.TryGetValue(toolName, out var reg))
            throw new KeyNotFoundException($"Tool '{toolName}' is not registered.");
        return ToolSchemaGenerator.GenerateSchema(reg.SettingsType);
    }

    public bool TryGetRegistration(string toolName, out ToolRegistration registration)
    {
        var found = _tools.TryGetValue(toolName, out var reg);
        registration = reg!;
        return found;
    }
}
