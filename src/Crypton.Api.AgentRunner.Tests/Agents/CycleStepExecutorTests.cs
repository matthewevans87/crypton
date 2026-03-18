using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using AgentRunner.Tests.Mocks;
using Moq;
using Xunit;

namespace AgentRunner.Tests.Agents;

public class CycleStepExecutorTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;
    private readonly MailboxRouter _mailboxRouter;
    private readonly Mock<IAgentContextBuilder> _contextBuilder;
    private readonly Mock<IAgentInvoker> _agentInvoker;
    private readonly Mock<IEventLogger> _logger;

    public CycleStepExecutorTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"executor_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        var storageConfig = new StorageConfig
        {
            BasePath = _tempPath,
            CyclesPath = "cycles",
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MemoryPath = "memory",
            ArchiveRetentionCount = 5
        };

        _artifactManager = new ArtifactManager(storageConfig);
        _mailboxManager = new MailboxManager(storageConfig);
        _mailboxRouter = new MailboxRouter(_mailboxManager);
        _contextBuilder = new Mock<IAgentContextBuilder>();
        _agentInvoker = new Mock<IAgentInvoker>();
        _logger = new Mock<IEventLogger>();

        _contextBuilder
            .Setup(b => b.BuildContext(It.IsAny<LoopState>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new AgentContext { AgentName = "plan", CycleId = "test_cycle" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    private CycleStepExecutor MakeExecutor() =>
        new(_contextBuilder.Object, _agentInvoker.Object, _artifactManager, _mailboxRouter, _logger.Object);

    private CycleContext MakeCycle()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();
        return new CycleContext { CycleId = cycleId };
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentSucceeds_ReturnsSuccess()
    {
        var cycle = MakeCycle();
        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() });

        var executor = MakeExecutor();
        var result = await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, CancellationToken.None);

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentSucceeds_SavesArtifact()
    {
        var cycle = MakeCycle();
        var planContent = MockPlanAgent.GeneratePlan();
        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = planContent });

        var executor = MakeExecutor();
        await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, CancellationToken.None);

        var saved = _artifactManager.ReadArtifact(cycle.CycleId, "plan.md");
        Assert.Equal(planContent, saved);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentFails_ReturnsFailed()
    {
        var cycle = MakeCycle();
        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = false, Error = "Timeout connecting to Ollama" });

        var executor = MakeExecutor();
        var result = await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Contains("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenArtifactValidationFails_ReturnsFailed()
    {
        var cycle = MakeCycle();
        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = "This is not a valid plan — missing all sections" });

        var executor = MakeExecutor();
        var result = await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, CancellationToken.None);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationCancelled_ReturnsTimeout()
    {
        var cycle = MakeCycle();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ThrowsAsync(new OperationCanceledException());

        var executor = MakeExecutor();
        var result = await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, cts.Token);

        Assert.Equal(StepOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsStartAndEndTime()
    {
        var cycle = MakeCycle();
        var before = DateTime.UtcNow;
        _agentInvoker
            .Setup(i => i.InvokeAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(new AgentInvocationResult { Success = true, Output = MockPlanAgent.GeneratePlan() });

        var executor = MakeExecutor();
        var result = await executor.ExecuteAsync(LoopState.Plan, cycle, null, null, null, CancellationToken.None);

        Assert.True(result.StartTime >= before);
        Assert.True(result.EndTime >= result.StartTime);
    }
}
