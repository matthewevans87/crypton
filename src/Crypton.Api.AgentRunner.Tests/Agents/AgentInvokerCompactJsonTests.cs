using AgentRunner.Configuration;
using AgentRunner.Domain;
using Xunit;

namespace AgentRunner.Tests.Agents;

/// <summary>Tests for domain types (replaces AgentInvokerCompactJsonTests).</summary>
public class DomainTypesTests
{
    [Fact]
    public void LoopState_HasExpectedValues()
    {
        var values = Enum.GetValues<LoopState>();
        Assert.Contains(LoopState.Idle, values);
        Assert.Contains(LoopState.Plan, values);
        Assert.Contains(LoopState.Research, values);
        Assert.Contains(LoopState.Analyze, values);
        Assert.Contains(LoopState.Synthesize, values);
        Assert.Contains(LoopState.Evaluate, values);
        Assert.Contains(LoopState.WaitingForNextCycle, values);
    }

    [Fact]
    public void MailboxMessage_RecordEquals_Works()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new MailboxMessage("plan", "research", "Hello", ts);
        var b = new MailboxMessage("plan", "research", "Hello", ts);
        Assert.Equal(a, b);
    }

    [Fact]
    public void MailboxMessage_WithMutation_Works()
    {
        var ts = DateTimeOffset.UtcNow;
        var original = new MailboxMessage("plan", "research", "Hello", ts);
        var mutated = original with { Content = "Updated" };
        Assert.Equal("Updated", mutated.Content);
        Assert.Equal("Hello", original.Content);
    }

    [Fact]
    public void StartupValidationResult_IsValid_ReflectsErrorList()
    {
        var valid = new StartupValidationResult(true, false, []);
        Assert.True(valid.IsValid);
        Assert.Empty(valid.Errors);

        var invalid = new StartupValidationResult(false, true, ["Missing config"]);
        Assert.False(invalid.IsValid);
        Assert.Single(invalid.Errors);
    }
}
