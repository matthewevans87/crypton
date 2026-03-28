using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Orchestration;
using Xunit;

namespace AgentRunner.Tests.Agents;

/// <summary>Tests for ArtifactValidator (replaces CycleStepExecutorTests).</summary>
public class ArtifactValidatorTests
{
    [Theory]
    [InlineData(LoopState.Plan, "## 1. Meta-Signals\n## 2. Macro Market Conditions\n## 3. Technical Signals\n## 4. On-Chain Signals\n## 5. News & Social Signals\n## 6. Research Agenda\n## 7. Signals Deprioritized", true)]
    [InlineData(LoopState.Plan, "This is not a plan", false)]
    [InlineData(LoopState.Plan, "", false)]
    public void Validate_Plan_ReturnsCorrectResult(LoopState state, string content, bool expected)
    {
        var result = ArtifactValidator.Validate(state, content);
        Assert.Equal(expected, result.IsValid);
    }

    [Theory]
    [InlineData(LoopState.Research, "# Research\n## Investigation Findings\n## Data Sources", true)]
    [InlineData(LoopState.Research, "just some text", false)]
    public void Validate_Research_ReturnsCorrectResult(LoopState state, string content, bool expected)
    {
        var result = ArtifactValidator.Validate(state, content);
        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void Validate_Idle_ReturnsTrue()
    {
        Assert.True(ArtifactValidator.Validate(LoopState.Idle, "anything").IsValid);
    }

    [Fact]
    public void Validate_NullContent_ReturnsFalse()
    {
        Assert.False(ArtifactValidator.Validate(LoopState.Plan, null!).IsValid);
    }
}
