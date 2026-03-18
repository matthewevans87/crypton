using AgentRunner.Agents;
using AgentRunner.Configuration;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using Xunit;

namespace AgentRunner.Tests.Mailbox;

public class MailboxRouterTests : IDisposable
{
    private readonly string _tempPath;
    private readonly MailboxManager _mailboxManager;
    private readonly MailboxRouter _router;

    public MailboxRouterTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"router_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        var config = new StorageConfig
        {
            BasePath = _tempPath,
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MaxMailboxMessages = 5
        };
        _mailboxManager = new MailboxManager(config);
        _router = new MailboxRouter(_mailboxManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    [Fact]
    public async Task RouteAsync_WhenResultFailed_DoesNotDeposit()
    {
        var result = new AgentInvocationResult { Success = false, Output = "<mailbox_to_research>hello</mailbox_to_research>" };

        await _router.RouteAsync(LoopState.Plan, result);

        var messages = _mailboxManager.GetMessages("research", 5);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task RouteAsync_WhenOutputEmpty_DoesNotDeposit()
    {
        var result = new AgentInvocationResult { Success = true, Output = "" };

        await _router.RouteAsync(LoopState.Plan, result);

        var messages = _mailboxManager.GetMessages("research", 5);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task RouteAsync_PlanState_DepositsForwardToResearch()
    {
        var result = new AgentInvocationResult
        {
            Success = true,
            Output = "<mailbox_to_research>Research this topic</mailbox_to_research>"
        };

        await _router.RouteAsync(LoopState.Plan, result);

        var messages = _mailboxManager.GetMessages("research", 5);
        Assert.Single(messages);
        Assert.Equal("Research this topic", messages[0].Content);
        Assert.Equal(MessageType.Forward, messages[0].Type);
    }

    [Fact]
    public async Task RouteAsync_PlanState_UsesTagFallbackWhenTagAbsent()
    {
        var result = new AgentInvocationResult { Success = true, Output = "no tags here" };

        await _router.RouteAsync(LoopState.Plan, result);

        var messages = _mailboxManager.GetMessages("research", 5);
        Assert.Single(messages);
        Assert.Equal("No forward message", messages[0].Content);
    }

    [Fact]
    public async Task RouteAsync_ResearchState_DepositsForwardToAnalysisAndFeedbackToPlan()
    {
        var result = new AgentInvocationResult
        {
            Success = true,
            Output = "<mailbox_to_analysis>Analysis input</mailbox_to_analysis><feedback>Adjust focus</feedback>"
        };

        await _router.RouteAsync(LoopState.Research, result);

        var analysisMessages = _mailboxManager.GetMessages("analysis", 5);
        Assert.Single(analysisMessages);
        Assert.Equal("Analysis input", analysisMessages[0].Content);
        Assert.Equal(MessageType.Forward, analysisMessages[0].Type);

        var planMessages = _mailboxManager.GetMessages("plan", 5);
        Assert.Single(planMessages);
        Assert.Equal("Adjust focus", planMessages[0].Content);
        Assert.Equal(MessageType.Feedback, planMessages[0].Type);
    }

    [Fact]
    public async Task RouteAsync_UnknownState_DoesNotDeposit()
    {
        var result = new AgentInvocationResult { Success = true, Output = "some output" };

        await _router.RouteAsync(LoopState.Idle, result);

        // No crash, no deposits — verify by checking a couple mailboxes
        Assert.Empty(_mailboxManager.GetMessages("research", 5));
        Assert.Empty(_mailboxManager.GetMessages("plan", 5));
    }
}
