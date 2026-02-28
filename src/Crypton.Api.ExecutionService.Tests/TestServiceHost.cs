using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crypton.Api.ExecutionService.Tests;

/// <summary>
/// Builds a fully in-process test host for the Execution Service,
/// wiring up mock adapters and in-memory infrastructure so full
/// execution scenarios can be tested without Docker or network calls.
/// </summary>
public sealed class TestServiceHost : IAsyncDisposable
{
    private readonly IHost _host;

    internal TestServiceHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public static TestServiceHostBuilder Create() => new();

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public sealed class TestServiceHostBuilder
{
    public TestServiceHost Build()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                // TODO: Register mock adapters, in-memory event log, fake clock, etc.
            })
            .Build();

        return new TestServiceHost(host);
    }
}
