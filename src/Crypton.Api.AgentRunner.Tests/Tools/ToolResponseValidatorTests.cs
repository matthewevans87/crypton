using AgentRunner.Configuration;
using AgentRunner.Domain;
using Xunit;

namespace AgentRunner.Tests.Tools;

/// <summary>Tests for ArtifactValidator synthesis output validation.</summary>
public class SynthesisValidatorTests
{
    [Fact]
    public void Validate_ValidStrategyJson_ReturnsTrue()
    {
        var json = """{"mode":"paper","validity_window":"24h","posture":"neutral","portfolio_risk":{"max_drawdown_pct":0.1,"daily_loss_limit_usd":100.0,"max_total_exposure_pct":0.5,"max_per_position_pct":0.25},"positions":[]}""";
        Assert.True(AgentRunner.Orchestration.ArtifactValidator.Validate(LoopState.Synthesize, json).IsValid);
    }

    [Fact]
    public void Validate_EmptyJson_ReturnsFalse()
    {
        Assert.False(AgentRunner.Orchestration.ArtifactValidator.Validate(LoopState.Synthesize, "{}").IsValid);
    }

    [Fact]
    public void Validate_MissingRequiredFields_ReturnsFalse()
    {
        Assert.False(AgentRunner.Orchestration.ArtifactValidator.Validate(LoopState.Synthesize, """{"strategy_version":"1.0"}""").IsValid);
    }
}
