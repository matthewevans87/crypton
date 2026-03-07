namespace Crypton.Configuration;

/// <summary>
/// Immutable snapshot of a service's degraded-state metadata.
/// </summary>
/// <param name="IsDegraded">Whether the service is currently degraded.</param>
/// <param name="Errors">Current degraded-state errors.</param>
/// <param name="LastTransitionAt">UTC timestamp of the most recent state transition.</param>
public sealed record DegradedStateSnapshot(
    bool IsDegraded,
    IReadOnlyList<string> Errors,
    DateTimeOffset? LastTransitionAt);

/// <summary>
/// Shared read/write contract for degraded-state storage.
/// </summary>
public interface IDegradedStateStore
{
    bool IsDegraded { get; }
    IReadOnlyList<string> Errors { get; }
    DateTimeOffset? LastTransitionAt { get; }

    void EnterDegraded(IEnumerable<string> errors);
    void ClearDegraded();
    DegradedStateSnapshot GetSnapshot();
}

/// <summary>
/// Thread-safe in-memory degraded-state store shared by service implementations.
/// </summary>
public sealed class DegradedStateStore : IDegradedStateStore
{
    private readonly object _lock = new();
    private bool _isDegraded;
    private IReadOnlyList<string> _errors = [];
    private DateTimeOffset? _lastTransitionAt;

    public bool IsDegraded
    {
        get
        {
            lock (_lock)
            {
                return _isDegraded;
            }
        }
    }

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_lock)
            {
                return _errors;
            }
        }
    }

    public DateTimeOffset? LastTransitionAt
    {
        get
        {
            lock (_lock)
            {
                return _lastTransitionAt;
            }
        }
    }

    public void EnterDegraded(IEnumerable<string> errors)
    {
        lock (_lock)
        {
            _isDegraded = true;
            _errors = errors.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            _lastTransitionAt = DateTimeOffset.UtcNow;
        }
    }

    public void ClearDegraded()
    {
        lock (_lock)
        {
            _isDegraded = false;
            _errors = [];
            _lastTransitionAt = DateTimeOffset.UtcNow;
        }
    }

    public DegradedStateSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new DegradedStateSnapshot(_isDegraded, _errors, _lastTransitionAt);
        }
    }
}

/// <summary>
/// One named prerequisite check used by recovery validators.
/// </summary>
/// <param name="Name">Human-readable check name.</param>
/// <param name="Passed">True when the check succeeded.</param>
/// <param name="Error">Failure detail when <paramref name="Passed"/> is false.</param>
public sealed record RecoveryCheckResult(string Name, bool Passed, string? Error = null);

/// <summary>
/// Aggregated recovery validation result.
/// </summary>
public sealed record RecoveryValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<RecoveryCheckResult> Checks { get; init; } = [];

    public IReadOnlyList<string> Errors =>
        Checks.Where(c => !c.Passed)
              .Select(c => string.IsNullOrWhiteSpace(c.Error) ? c.Name : $"{c.Name}: {c.Error}")
              .ToList();

    public static RecoveryValidationResult Success(params RecoveryCheckResult[] checks)
    {
        return new RecoveryValidationResult
        {
            IsValid = true,
            Checks = checks
        };
    }

    public static RecoveryValidationResult Failure(params RecoveryCheckResult[] checks)
    {
        return new RecoveryValidationResult
        {
            IsValid = false,
            Checks = checks
        };
    }
}

/// <summary>
/// Service-specific prerequisite validation before degraded-state recovery.
/// </summary>
public interface IRecoveryValidator
{
    Task<RecoveryValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}