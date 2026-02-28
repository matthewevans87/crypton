using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Strategy;

public sealed class StrategyServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StrategyServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"crypton_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private (StrategyService service, InMemoryEventLogger eventLogger) CreateService(
        int reloadLatencyMs = 0,
        int validityCheckIntervalMs = 100,
        string? watchFileName = "strategy.json")
    {
        var watchPath = Path.Combine(_tempDir, watchFileName!);

        var config = Options.Create(new ExecutionServiceConfig
        {
            Strategy = new StrategyConfig
            {
                WatchPath = watchPath,
                ReloadLatencyMs = reloadLatencyMs,
                ValidityCheckIntervalMs = validityCheckIntervalMs
            }
        });

        var eventLogger = new InMemoryEventLogger();
        var validator = new StrategyValidator();
        var parser = new ConditionParser();
        var logger = NullLogger<StrategyService>.Instance;

        return (new StrategyService(config, validator, parser, eventLogger, logger), eventLogger);
    }

    private static string ValidStrategyJson(
        string mode = "paper",
        string posture = "moderate",
        DateTimeOffset? validityWindow = null,
        IReadOnlyList<StrategyPosition>? positions = null)
    {
        var vw = validityWindow ?? DateTimeOffset.UtcNow.AddHours(1);
        var pos = positions ?? [new StrategyPosition
        {
            Id = "pos-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market"
        }];

        var doc = new StrategyDocument
        {
            Mode = mode,
            Posture = posture,
            ValidityWindow = vw,
            PortfolioRisk = new PortfolioRisk
            {
                MaxDrawdownPct = 0.1m,
                DailyLossLimitUsd = 500m,
                MaxTotalExposurePct = 0.8m,
                MaxPerPositionPct = 0.2m
            },
            Positions = pos
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    private void WriteStrategyFile(string json, string fileName = "strategy.json")
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), json);
    }

    private static async Task<T> WaitForAsync<T>(Task<T> task, string description, int timeoutMs = 5000)
    {
        try
        {
            return await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Timed out waiting for: {description}");
        }
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadValidStrategy_SetsStateActive_AndFiresOnStrategyLoaded()
    {
        var (svc, eventLogger) = CreateService();
        WriteStrategyFile(ValidStrategyJson());

        var tcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnStrategyLoaded += doc => { tcs.TrySetResult(doc); return Task.CompletedTask; };

        await svc.StartAsync(CancellationToken.None);

        var loaded = await WaitForAsync(tcs.Task, "OnStrategyLoaded fired");

        svc.State.Should().Be(StrategyState.Active);
        svc.ActiveStrategy.Should().NotBeNull();
        svc.ActiveStrategyId.Should().NotBeNullOrEmpty();
        loaded.Posture.Should().Be("moderate");

        eventLogger.Events
            .Should().Contain(e => e.EventType == EventTypes.StrategyLoaded);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task LoadInvalidStrategy_KeepsPriorState_AndLogsRejected()
    {
        var (svc, eventLogger) = CreateService();

        // Write an invalid JSON to the file
        WriteStrategyFile("{ this is not valid json }");

        await svc.StartAsync(CancellationToken.None);

        // Wait a short time for the async load to attempt
        await Task.Delay(500);

        svc.State.Should().Be(StrategyState.None);
        svc.ActiveStrategy.Should().BeNull();

        eventLogger.Events
            .Should().Contain(e => e.EventType == EventTypes.StrategyRejected);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task LoadStrategyWithPastValidityWindow_LogsRejectedWithExpiredMessage()
    {
        var (svc, eventLogger) = CreateService();

        // validity_window in the past — validator will reject it
        var pastValidity = DateTimeOffset.UtcNow.AddHours(-1);
        var json = ValidStrategyJson(validityWindow: pastValidity);
        WriteStrategyFile(json);

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        svc.State.Should().Be(StrategyState.None);
        eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.StrategyRejected);

        var rejectedEvent = eventLogger.Events.First(e => e.EventType == EventTypes.StrategyRejected);
        var reason = rejectedEvent.Data!["reason"]?.ToString();
        reason.Should().Contain("already expired");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task SwappingStrategy_EmitsStrategySwappedEvent_WithBothIds()
    {
        var (svc, eventLogger) = CreateService(reloadLatencyMs: 0);

        // Prepare first strategy
        WriteStrategyFile(ValidStrategyJson(posture: "moderate"));

        var firstLoadTcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoadTcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadCount = 0;

        svc.OnStrategyLoaded += doc =>
        {
            loadCount++;
            if (loadCount == 1) firstLoadTcs.TrySetResult(doc);
            else secondLoadTcs.TrySetResult(doc);
            return Task.CompletedTask;
        };

        await svc.StartAsync(CancellationToken.None);

        var first = await WaitForAsync(firstLoadTcs.Task, "first strategy loaded");
        var firstId = svc.ActiveStrategyId;
        firstId.Should().NotBeNullOrEmpty();

        // Write a different strategy to trigger hot-reload
        WriteStrategyFile(ValidStrategyJson(posture: "aggressive"));

        var second = await WaitForAsync(secondLoadTcs.Task, "second strategy loaded");
        var secondId = svc.ActiveStrategyId;

        second.Posture.Should().Be("aggressive");
        secondId.Should().NotBe(firstId);

        eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.StrategySwapped);
        var swapEvent = eventLogger.Events.First(e => e.EventType == EventTypes.StrategySwapped);
        swapEvent.Data!["strategy_id"]?.ToString().Should().Be(secondId);
        swapEvent.Data!["previous_id"]?.ToString().Should().Be(firstId);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task ValidityWindowExpiry_TransitionsToExpired_AndLogsStrategyExpired()
    {
        var (svc, eventLogger) = CreateService(validityCheckIntervalMs: 100);

        // Strategy expires in ~300ms
        var json = ValidStrategyJson(validityWindow: DateTimeOffset.UtcNow.AddMilliseconds(300));
        WriteStrategyFile(json);

        var loadTcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expiredTcs = new TaskCompletionSource<StrategyState>(TaskCreationOptions.RunContinuationsAsynchronously);

        svc.OnStrategyLoaded += doc => { loadTcs.TrySetResult(doc); return Task.CompletedTask; };
        svc.OnStateChanged += state => { expiredTcs.TrySetResult(state); return Task.CompletedTask; };

        await svc.StartAsync(CancellationToken.None);

        await WaitForAsync(loadTcs.Task, "initial strategy load", timeoutMs: 5000);
        svc.State.Should().Be(StrategyState.Active);

        // Wait for expiry transition (validity window 300ms + monitor 100ms + buffer)
        var newState = await WaitForAsync(expiredTcs.Task, "state transitioned to Expired", timeoutMs: 3000);
        newState.Should().Be(StrategyState.Expired);
        svc.State.Should().Be(StrategyState.Expired);

        eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.StrategyExpired);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task StrategyFileDoesNotExist_StateRemainsNone()
    {
        var (svc, eventLogger) = CreateService();
        // No strategy file written — directory exists but file does not

        await svc.StartAsync(CancellationToken.None);

        // Wait a short time to ensure no async load started
        await Task.Delay(300);

        svc.State.Should().Be(StrategyState.None);
        svc.ActiveStrategy.Should().BeNull();
        eventLogger.Events.Should().BeEmpty();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task EntryConditionParseError_RejectsStrategy_WithDescriptiveMessage()
    {
        var (svc, eventLogger) = CreateService();

        // Position with conditional type and an unparseable entry_condition
        var positions = new List<StrategyPosition>
        {
            new()
            {
                Id = "bad-pos",
                Asset = "BTC/USD",
                Direction = "long",
                AllocationPct = 0.1m,
                EntryType = "conditional",
                EntryCondition = "INVALID_CONDITION ??? bad syntax !!!"
            }
        };

        WriteStrategyFile(ValidStrategyJson(positions: positions));

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        svc.State.Should().Be(StrategyState.None);

        eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.StrategyRejected);
        var rejectedEvent = eventLogger.Events.First(e => e.EventType == EventTypes.StrategyRejected);
        var reason = rejectedEvent.Data!["reason"]?.ToString();
        reason.Should().Contain("entry_condition parse error");
        reason.Should().Contain("bad-pos");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    [Fact]
    public async Task LoadValidStrategy_ComputesStableStrategyId()
    {
        var (svc, _) = CreateService();
        var json = ValidStrategyJson();
        WriteStrategyFile(json);

        var tcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnStrategyLoaded += doc => { tcs.TrySetResult(doc); return Task.CompletedTask; };

        await svc.StartAsync(CancellationToken.None);
        await WaitForAsync(tcs.Task, "strategy loaded");

        var id1 = svc.ActiveStrategyId;
        id1.Should().NotBeNullOrEmpty();
        id1!.Length.Should().Be(16); // first 16 hex chars of SHA256

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
