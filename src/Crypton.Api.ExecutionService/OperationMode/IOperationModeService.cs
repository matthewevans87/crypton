namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Exposes the read/write surface of OperationModeService for dependency injection and testing.
/// </summary>
public interface IOperationModeService
{
    string CurrentMode { get; }
    Task PromoteToLiveAsync(string operatorNote = "", CancellationToken ct = default);
    Task DemoteToPaperAsync(string operatorNote = "", CancellationToken ct = default);
}
