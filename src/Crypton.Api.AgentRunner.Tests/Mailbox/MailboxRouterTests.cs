using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Mailbox;

/// <summary>Tests for state-based artifact routing via ArtifactValidator.</summary>
public class ArtifactValidationRoutingTests
{
    [Theory]
    [InlineData(LoopState.Idle)]
    [InlineData(LoopState.WaitingForNextCycle)]
    public void Validate_NonAgentStates_AlwaysReturnTrue(LoopState state)
    {
        Assert.True(AgentRunner.Orchestration.ArtifactValidator.Validate(state, "any content").IsValid);
    }

    [Fact]
    public void Validate_Synthesize_ValidStrategyJson_ReturnsTrue()
    {
        var json = """{"mode":"paper","validity_window":"24h","posture":"neutral","portfolio_risk":{"max_drawdown_pct":0.1,"daily_loss_limit_usd":100.0,"max_total_exposure_pct":0.5,"max_per_position_pct":0.25},"positions":[]}""";
        var result = AgentRunner.Orchestration.ArtifactValidator.Validate(LoopState.Synthesize, json);
        Assert.True(result.IsValid);
    }
}
