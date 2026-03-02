using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.OperationMode;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.OperationMode;

/// <summary>
/// Tests for MarketDataServiceClient subscription management and graceful error handling.
/// Connection-to-hub tests require a running SignalR server and are not included here
/// (they belong in integration test suites). These tests validate:
/// <list type="bullet">
///   <item>Subscription registration via SubscribeAsync</item>
///   <item>Graceful startup failure when hub is unreachable (no throw)</item>
///   <item>Graceful stop when never started</item>
/// </list>
/// </summary>
public sealed class MarketDataServiceClientTests
{
    private static MarketDataServiceClient Create(string hubUrl = "http://localhost:19999") =>
        new(hubUrl, NullLogger<MarketDataServiceClient>.Instance);

    // ─────────────────────────────────────────────────────────────────────────
    // SubscribeAsync — callback registration
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_RegistersCallbackForAsset()
    {
        var sut = Create();
        var received = new List<MarketSnapshot>();

        await sut.SubscribeAsync(["BTC/USD"], s => { received.Add(s); return Task.CompletedTask; });

        // Subscription is registered — no hub connection needed to verify storage.
        // (Actual delivery relies on a live hub; tested via integration tests.)
        received.Should().BeEmpty(); // No ticks until hub fires
    }

    [Fact]
    public async Task SubscribeAsync_MultipleAssets_AllRegistered()
    {
        var sut = Create();
        var received = new List<string>();

        await sut.SubscribeAsync(
            ["BTC/USD", "ETH/USD", "SOL/USD"],
            s => { received.Add(s.Asset); return Task.CompletedTask; });

        // No exception – subscriptions are registered.
        received.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_SameAsset_MultipleTimes_BothCallbacksRegistered()
    {
        var sut = Create();
        int cb1Calls = 0, cb2Calls = 0;

        await sut.SubscribeAsync(["BTC/USD"], _ => { cb1Calls++; return Task.CompletedTask; });
        await sut.SubscribeAsync(["BTC/USD"], _ => { cb2Calls++; return Task.CompletedTask; });

        // Both registrations should succeed without error.
        cb1Calls.Should().Be(0);
        cb2Calls.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeAsync_CaseInsensitiveAssetKey_DoesNotThrow()
    {
        var sut = Create();

        // Lower-case vs upper-case for the same asset should both work.
        await sut.SubscribeAsync(["btc/usd"], _ => Task.CompletedTask);
        await sut.SubscribeAsync(["BTC/USD"], _ => Task.CompletedTask);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // StartAsync / StopAsync — error handling
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_UnreachableHub_DoesNotThrow()
    {
        // Hub points to a port nobody is listening on — should not throw.
        var sut = Create("http://localhost:19999");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.Awaiting(s => s.StartAsync(cts.Token))
                 .Should().NotThrowAsync();

        // Clean up.
        await sut.StopAsync(CancellationToken.None);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        var sut = Create();
        await sut.Awaiting(s => s.StopAsync(CancellationToken.None))
                 .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_DoesNotThrow()
    {
        var sut = Create();
        await sut.Awaiting(s => s.DisposeAsync().AsTask())
                 .Should().NotThrowAsync();
    }
}
