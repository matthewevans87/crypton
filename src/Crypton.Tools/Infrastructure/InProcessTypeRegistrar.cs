using Spectre.Console.Cli;

namespace Crypton.Tools.Infrastructure;

/// <summary>
/// A minimal <see cref="ITypeRegistrar"/> that returns pre-constructed instances and falls back
/// to <see cref="Activator.CreateInstance"/> for anything else.  Used by
/// <see cref="InProcessVirtualTerminal"/> to inject a capturing <see cref="Spectre.Console.IAnsiConsole"/>
/// into tool command constructors without a full DI container.
/// </summary>
internal sealed class InProcessTypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, object> _instances = new();

    public void RegisterInstance(Type type, object instance) =>
        _instances[type] = instance;

    // Not needed for pre-built instances — no-op.
    public void Register(Type service, Type implementation) { }
    public void RegisterLazy(Type service, Func<object> factory) { }

    public ITypeResolver Build() => new InProcessTypeResolver(_instances);
}

internal sealed class InProcessTypeResolver(Dictionary<Type, object> instances) : ITypeResolver
{
    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        if (instances.TryGetValue(type, out var existing)) return existing;

        // Spectre.Console.Cli internally resolves several IEnumerable<IXxx> types.
        // Return an empty array for any such request we don't have a registration for.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var elementType = type.GetGenericArguments()[0];
            return Array.CreateInstance(elementType, 0);
        }

        try { return Activator.CreateInstance(type); } catch { return null; }
    }

    public void Dispose() { }
}
