using Microsoft.Extensions.Hosting;

namespace Crypton.Api.ExecutionService.Resilience;

/// <summary>
/// Hosted service that wires <see cref="FailureTracker.OnSafeModeTriggered"/> to
/// <see cref="SafeModeController.ActivateAsync"/> after the DI container has resolved
/// both singletons, avoiding circular constructor dependencies.
/// </summary>
internal sealed class ResilienceWiring : IHostedService
{
    private readonly FailureTracker _failureTracker;
    private readonly SafeModeController _safeModeController;

    public ResilienceWiring(FailureTracker failureTracker, SafeModeController safeModeController)
    {
        _failureTracker = failureTracker;
        _safeModeController = safeModeController;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _failureTracker.OnSafeModeTriggered += reason =>
            _safeModeController.ActivateAsync(reason, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
