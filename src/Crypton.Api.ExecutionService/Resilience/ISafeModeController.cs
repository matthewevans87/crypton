namespace Crypton.Api.ExecutionService.Resilience;

/// <summary>
/// Exposes the read/write surface of SafeModeController for dependency injection and testing.
/// </summary>
public interface ISafeModeController
{
    bool IsActive { get; }
    string? Reason { get; }
    Task ActivateAsync(string reason, CancellationToken ct = default);
    Task DeactivateAsync(CancellationToken ct = default);
}
