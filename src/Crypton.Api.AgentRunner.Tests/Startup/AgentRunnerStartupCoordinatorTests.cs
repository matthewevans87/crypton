using AgentRunner.Configuration;
using Xunit;

namespace AgentRunner.Tests.Startup;

/// <summary>Tests for configuration-level startup validation.</summary>
public class ConfigurationStartupValidationTests
{
    [Fact]
    public void AgentRunnerConfig_DefaultValues_AreSet()
    {
        var config = new AgentRunnerConfig();

        Assert.NotNull(config.Cycle);
        Assert.NotNull(config.Resilience);
        Assert.NotNull(config.Agents);
        Assert.NotNull(config.Tools);
        Assert.NotNull(config.Ollama);
        Assert.NotNull(config.Storage);
        Assert.NotNull(config.Api);
        Assert.NotNull(config.Logging);
    }

    [Fact]
    public void AgentRunnerConfig_GetAgentSettings_ThrowsWhenNotConfigured()
    {
        var config = new AgentRunnerConfig();
        Assert.Throws<InvalidOperationException>(() => config.GetAgentSettings("plan"));
    }

    [Fact]
    public void AgentRunnerConfig_GetAgentSettings_ReturnsSettings()
    {
        var config = new AgentRunnerConfig
        {
            Agents = new Dictionary<string, AgentSettings>
            {
                ["plan"] = new AgentSettings { Model = "qwen3:35b", TimeoutMinutes = 30 }
            }
        };

        var settings = config.GetAgentSettings("plan");
        Assert.Equal("qwen3:35b", settings.Model);
    }

    [Fact]
    public void AgentRunnerConfig_GetAgentSettings_IsCaseInsensitive()
    {
        var config = new AgentRunnerConfig
        {
            Agents = new Dictionary<string, AgentSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLAN"] = new AgentSettings { Model = "qwen3:35b" }
            }
        };

        var settings = config.GetAgentSettings("plan");
        Assert.Equal("qwen3:35b", settings.Model);
    }
}
