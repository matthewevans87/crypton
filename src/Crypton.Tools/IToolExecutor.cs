namespace Crypton.Tools;

/// <summary>Wraps a tool invocation with retry, circuit-breaker, and timeout logic.</summary>
public interface IToolExecutor
{
    Task<T> ExecuteWithResilienceAsync<T>(
        string toolName,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default);
}
