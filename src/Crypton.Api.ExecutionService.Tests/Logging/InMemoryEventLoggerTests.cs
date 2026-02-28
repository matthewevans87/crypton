using Crypton.Api.ExecutionService.Logging;
using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Logging;

public sealed class InMemoryEventLoggerTests
{
    private readonly InMemoryEventLogger _sut = new();

    [Fact]
    public async Task LogAsync_AddsEventToList()
    {
        await _sut.LogAsync(EventTypes.ServiceStarted, "paper");
        _sut.Events.Should().ContainSingle();
        _sut.Events[0].EventType.Should().Be(EventTypes.ServiceStarted);
        _sut.Events[0].Mode.Should().Be("paper");
    }

    [Fact]
    public async Task LogAsync_EventsReturnedInInsertionOrder()
    {
        await _sut.LogAsync(EventTypes.ServiceStarted, "paper");
        await _sut.LogAsync(EventTypes.StrategyLoaded, "paper");
        await _sut.LogAsync(EventTypes.OrderPlaced, "paper");

        _sut.Events.Select(e => e.EventType).Should().ContainInOrder(
            EventTypes.ServiceStarted,
            EventTypes.StrategyLoaded,
            EventTypes.OrderPlaced);
    }

    [Fact]
    public async Task Clear_EmptiesTheList()
    {
        await _sut.LogAsync(EventTypes.ServiceStarted, "paper");
        await _sut.LogAsync(EventTypes.ServiceStopped, "paper");

        _sut.Clear();

        _sut.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LogAsync_ReturnsCompletedTask()
    {
        var task = _sut.LogAsync(EventTypes.ServiceStarted, "paper");
        task.IsCompleted.Should().BeTrue();
        await task; // should not throw
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotCorruptList()
    {
        const int count = 50;
        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(() => _sut.LogAsync(EventTypes.OrderPlaced, "paper",
                new Dictionary<string, object?> { ["index"] = i })))
            .ToArray();

        await Task.WhenAll(tasks);

        _sut.Events.Should().HaveCount(count);
        _sut.Events.Should().AllSatisfy(e => e.EventType.Should().Be(EventTypes.OrderPlaced));
    }

    [Fact]
    public async Task Events_Property_ReturnsSnapshot_NotLiveReference()
    {
        await _sut.LogAsync(EventTypes.ServiceStarted, "paper");
        var snapshot = _sut.Events;

        await _sut.LogAsync(EventTypes.ServiceStopped, "paper");

        // The captured snapshot should still have only 1 item
        snapshot.Should().HaveCount(1);
        _sut.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task LogAsync_WithData_StoresData()
    {
        var data = new Dictionary<string, object?> { ["order_id"] = "abc123", ["qty"] = 1.5m };
        await _sut.LogAsync(EventTypes.OrderPlaced, "live", data);

        _sut.Events[0].Data.Should().ContainKey("order_id");
        _sut.Events[0].Data!["order_id"].Should().Be("abc123");
    }

    [Fact]
    public async Task LogAsync_WithNullData_IsAllowed()
    {
        await _sut.LogAsync(EventTypes.ServiceStarted, "paper", null);
        _sut.Events.Should().ContainSingle();
        _sut.Events[0].Data.Should().BeNull();
    }
}
