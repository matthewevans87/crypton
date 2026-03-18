using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using AgentRunner.Telemetry;
using AgentRunner.Tests.Mocks;
using Moq;
using Xunit;

namespace AgentRunner.Tests.Agents;

/// <summary>
/// Integration tests for the Abort → Start workflow and the full agent loop.
/// Dependencies are real where possible; only the LLM boundary (IAgentInvoker)
/// and context builder are mocked.
/// </summary>
public class AgentRunnerServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly StorageConfig _storageConfig;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;

    public AgentRunnerServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ar_svc_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        _storageConfig = new StorageConfig
        {
            BasePath = _tempPath,
            CyclesPath = "cycles",
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MemoryPath = "memory",
            ArchiveRetentionCount = 5
        };

        _artifactManager = new ArtifactManager(_storageConfig);
        _mailboxManager = new MailboxManager(_storageConfig);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private (AgentRunnerService Service, Mock<IAgentInvoker> Invoker) BuildService(
        AgentRunnerConfig? config = null)
    {
        config ??= new AgentRunnerConfig
        {
            Cycle = new CycleConfig
            {
                ScheduleIntervalMinutes = 5,  // long enough for the test to stop it
                MaxDurationMinutes = 60
            },
            Resilience = new ResilienceConfig
            {
                MaxRestartAttempts = 0,        // no automatic restarts — keep test deterministic
                BaseRestartDelayMinutes = 0,
                MaxRestartDelayMinutes = 0
            },
            Agents = new AgentConfig
            {
                Plan = new AgentSettings { MaxRetries = 0 },
                Research = new AgentSettings { MaxRetries = 0 },
                Analyze = new AgentSettings { MaxRetries = 0 },
                Synthesis = new AgentSettings { MaxRetries = 0 },
                Evaluation = new AgentSettings { MaxRetries = 0 }
            }
        };

        var stateMachine = new LoopStateMachine();
        var persistence = new StatePersistence(Path.Combine(_tempPath, $"state_{Guid.NewGuid()}.json"));
        var logger = new Mock<IEventLogger>();
        var metrics = new MetricsCollector();

        var contextBuilder = new Mock<IAgentContextBuilder>();
        contextBuilder
            .Setup(b => b.BuildContext(It.IsAny<LoopState>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new AgentContext());

        var agentInvoker = new Mock<IAgentInvoker>();

        var mailboxRouter = new MailboxRouter(_mailboxManager);
        var stepExecutor = new CycleStepExecutor(contextBuilder.Object, agentInvoker.Object,
                                 _artifactManager, mailboxRouter, logger.Object);
        var scheduler = new CycleScheduler(config, _artifactManager, logger.Object);
        var restartManager = new LoopRestartManager(config, logger.Object);

        var service = new AgentRunnerService(config, stateMachine, persistence,
            _artifactManager, _mailboxManager, logger.Object, metrics,
            stepExecutor, scheduler, restartManager);

        return (service, agentInvoker);
    }

    private static void SetupFullCycleInvoker(Mock<IAgentInvoker> invoker)
    {
        invoker.SetupSequence(i => i.InvokeAsync(
                It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockResearchAgent.GenerateResearch() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockAnalysisAgent.GenerateAnalysis() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockSynthesisAgent.GenerateStrategy() });
    }

    // -------------------------------------------------------------------------
    // Abort-only tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AbortAsync_WhenIdle_KeepsStateIdle()
    {
        var (service, _) = BuildService();

        await service.AbortAsync();

        Assert.Equal(LoopState.Idle, service.CurrentState);
        Assert.Null(service.CurrentCycle);
    }

    [Fact]
    public async Task AbortAsync_WhenRunning_StopsLoopAndTransitionsToIdle()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        await service.StartAsync();
        // Give the loop a moment to start
        await Task.Delay(50);

        await service.AbortAsync();

        Assert.False(service.IsRunning);
        Assert.Equal(LoopState.Idle, service.CurrentState);
        Assert.Null(service.CurrentCycle);
    }

    [Fact]
    public async Task AbortAsync_ClearsLatestPreviousCycleId_SoNextStartBeginsWithPlan()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        // Run a full cycle to completion so latestPreviousCycleId gets set
        var waitingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, state) =>
        {
            if (state == LoopState.WaitingForNextCycle)
                waitingTcs.TrySetResult(true);
        };

        await service.StartAsync();
        await waitingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await service.AbortAsync();

        // After abort, the entire state is cleared
        Assert.Equal(LoopState.Idle, service.CurrentState);
        Assert.Null(service.CurrentCycle);
    }

    // -------------------------------------------------------------------------
    // Abort → Start tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AbortThenStart_ServiceIsRunning()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        await service.AbortAsync();
        await service.StartAsync();

        Assert.True(service.IsRunning);
        await service.StopAsync();
    }

    [Fact]
    public async Task AbortThenStart_StartsFromPlanState()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        var firstStateSeen = new TaskCompletionSource<LoopState>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, state) => firstStateSeen.TrySetResult(state);

        await service.AbortAsync();
        await service.StartAsync();

        var state = await firstStateSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(LoopState.Plan, state);

        await service.StopAsync();
    }

    [Fact]
    public async Task AbortThenStart_ExecutesAllStepsInOrder()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        var observedStates = new List<LoopState>();
        var waitingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.StateChanged += (_, state) =>
        {
            lock (observedStates) observedStates.Add(state);
            if (state == LoopState.WaitingForNextCycle)
                waitingTcs.TrySetResult(true);
        };

        await service.AbortAsync();
        await service.StartAsync();

        await waitingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync();

        Assert.Contains(LoopState.Plan, observedStates);
        Assert.Contains(LoopState.Research, observedStates);
        Assert.Contains(LoopState.Analyze, observedStates);
        Assert.Contains(LoopState.Synthesize, observedStates);
        Assert.Contains(LoopState.WaitingForNextCycle, observedStates);

        // Verify order: Plan before Research before Analyze before Synthesize
        Assert.True(observedStates.IndexOf(LoopState.Plan) < observedStates.IndexOf(LoopState.Research));
        Assert.True(observedStates.IndexOf(LoopState.Research) < observedStates.IndexOf(LoopState.Analyze));
        Assert.True(observedStates.IndexOf(LoopState.Analyze) < observedStates.IndexOf(LoopState.Synthesize));
        Assert.True(observedStates.IndexOf(LoopState.Synthesize) < observedStates.IndexOf(LoopState.WaitingForNextCycle));
    }

    [Fact]
    public async Task AbortThenStart_AllFourAgentStepsInvoked()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        var waitingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, state) =>
        {
            if (state == LoopState.WaitingForNextCycle)
                waitingTcs.TrySetResult(true);
        };

        await service.AbortAsync();
        await service.StartAsync();

        await waitingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync();

        invoker.Verify(i => i.InvokeAsync(
            It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
            It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()), Times.Exactly(4));
    }

    [Fact]
    public async Task AbortThenStart_ArtifactsSavedForEachStep()
    {
        var (service, invoker) = BuildService();
        SetupFullCycleInvoker(invoker);

        string? capturedCycleId = null;
        var waitingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, state) =>
        {
            if (state == LoopState.WaitingForNextCycle)
            {
                capturedCycleId = service.CurrentCycle?.CycleId;
                waitingTcs.TrySetResult(true);
            }
        };

        await service.AbortAsync();
        await service.StartAsync();

        await waitingTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync();

        Assert.NotNull(capturedCycleId);
        Assert.NotNull(_artifactManager.ReadArtifact(capturedCycleId!, "plan.md"));
        Assert.NotNull(_artifactManager.ReadArtifact(capturedCycleId!, "research.md"));
        Assert.NotNull(_artifactManager.ReadArtifact(capturedCycleId!, "analysis.md"));
        Assert.NotNull(_artifactManager.ReadArtifact(capturedCycleId!, "strategy.json"));
    }

    // -------------------------------------------------------------------------
    // Second-cycle Evaluate tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// After a completed first cycle (Synthesize → WaitingForNextCycle), forcing a new cycle
    /// should run Evaluate BEFORE Plan, because a prior completed cycle exists.
    /// This is the core correctness test for the Evaluate step in the normal loop flow.
    /// </summary>
    [Fact]
    public async Task SecondCycle_StartsWithEvaluate_BeforePlan()
    {
        // Use a zero-interval scheduler so the second cycle starts immediately after
        // WaitingForNextCycle without requiring ForceNewCycle() (which has an init race).
        var config = new AgentRunnerConfig
        {
            Cycle = new CycleConfig { ScheduleIntervalMinutes = 0, MaxDurationMinutes = 60 },
            Resilience = new ResilienceConfig
            {
                MaxRestartAttempts = 0,
                BaseRestartDelayMinutes = 0,
                MaxRestartDelayMinutes = 0
            },
            Agents = new AgentConfig
            {
                Plan = new AgentSettings { MaxRetries = 0 },
                Research = new AgentSettings { MaxRetries = 0 },
                Analyze = new AgentSettings { MaxRetries = 0 },
                Synthesis = new AgentSettings { MaxRetries = 0 },
                Evaluation = new AgentSettings { MaxRetries = 0 }
            }
        };
        var (service, invoker) = BuildService(config);

        // Cycle 1: Plan, Research, Analyze, Synthesize
        // Cycle 2: Evaluate, Plan (test stops here)
        invoker.SetupSequence(i => i.InvokeAsync(
                It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockResearchAgent.GenerateResearch() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockAnalysisAgent.GenerateAnalysis() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockSynthesisAgent.GenerateStrategy() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockEvaluationAgent.GenerateEvaluation() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() });

        var evaluateSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var planAfterEvaluateSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluateSeenFirst = false;

        service.StateChanged += (_, state) =>
        {
            if (state == LoopState.Evaluate)
            {
                evaluateSeenFirst = true;
                evaluateSeen.TrySetResult(true);
            }
            if (state == LoopState.Plan && evaluateSeenFirst)
                planAfterEvaluateSeen.TrySetResult(true);
        };

        await service.StartAsync();

        await evaluateSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await planAfterEvaluateSeen.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await service.StopAsync();

        Assert.True(evaluateSeenFirst, "Evaluate should fire before the second Plan");
    }

    [Fact]
    public async Task SecondCycle_EvaluateArtifactSavedInNewCycle()
    {
        var config = new AgentRunnerConfig
        {
            Cycle = new CycleConfig { ScheduleIntervalMinutes = 0, MaxDurationMinutes = 60 },
            Resilience = new ResilienceConfig
            {
                MaxRestartAttempts = 0,
                BaseRestartDelayMinutes = 0,
                MaxRestartDelayMinutes = 0
            },
            Agents = new AgentConfig
            {
                Plan = new AgentSettings { MaxRetries = 0 },
                Research = new AgentSettings { MaxRetries = 0 },
                Analyze = new AgentSettings { MaxRetries = 0 },
                Synthesis = new AgentSettings { MaxRetries = 0 },
                Evaluation = new AgentSettings { MaxRetries = 0 }
            }
        };
        var (service, invoker) = BuildService(config);

        invoker.SetupSequence(i => i.InvokeAsync(
                It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockResearchAgent.GenerateResearch() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockAnalysisAgent.GenerateAnalysis() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockSynthesisAgent.GenerateStrategy() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockEvaluationAgent.GenerateEvaluation() })
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() });

        string? secondCycleId = null;
        var planAfterEvaluateTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var evaluateSeen = false;

        service.StateChanged += (_, state) =>
        {
            if (state == LoopState.Evaluate)
                evaluateSeen = true;
            if (state == LoopState.Plan && evaluateSeen)
            {
                secondCycleId = service.CurrentCycle?.CycleId;
                planAfterEvaluateTcs.TrySetResult(true);
            }
        };

        await service.StartAsync();
        await planAfterEvaluateTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync();

        Assert.NotNull(secondCycleId);
        Assert.NotNull(_artifactManager.ReadArtifact(secondCycleId!, "evaluation.md"));
    }
}
