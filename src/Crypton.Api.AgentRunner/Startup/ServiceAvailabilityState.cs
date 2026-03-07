using Crypton.Configuration;

namespace AgentRunner.Startup;

public sealed class ServiceAvailabilityState : IDegradedStateStore
{
    private readonly DegradedStateStore _store = new();

    public bool IsDegraded
    {
        get => _store.IsDegraded;
    }

    public IReadOnlyList<string> Errors
    {
        get => _store.Errors;
    }

    public DateTimeOffset? LastTransitionAt
    {
        get => _store.LastTransitionAt;
    }

    public void EnterDegraded(IEnumerable<string> errors)
    {
        _store.EnterDegraded(errors);
    }

    public void ClearDegraded()
    {
        _store.ClearDegraded();
    }

    public DegradedStateSnapshot GetSnapshot()
    {
        return _store.GetSnapshot();
    }
}
