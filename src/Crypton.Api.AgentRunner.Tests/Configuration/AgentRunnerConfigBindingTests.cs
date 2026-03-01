using AgentRunner.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentRunner.Tests.Configuration;

/// <summary>
/// Verifies that <see cref="AgentRunnerConfig"/> binds correctly from IConfiguration,
/// and that the standard .NET configuration precedence is respected:
///
///   command-line args  (highest)
///   environment vars   ← double-underscore (__) hierarchy
///   appsettings.json   ← defaults
///
/// Tests use ConfigurationBuilder with in-memory collections to simulate each
/// source without touching the file system or the real process environment.
/// </summary>
public class AgentRunnerConfigBindingTests
{
    // ────────────────────────────────────────────────────────────────
    // Default values from an empty configuration
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_EmptyConfiguration_UsesPropertyDefaults()
    {
        var config = BuildConfig();

        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal(60, result.Cycle.MinDurationMinutes);
        Assert.Equal(1440, result.Cycle.MaxDurationMinutes);
        Assert.Equal(360, result.Cycle.ScheduleIntervalMinutes);
        Assert.Equal(5, result.Resilience.MaxRestartAttempts);
        Assert.Equal(30, result.Tools.DefaultTimeoutSeconds);
        Assert.Equal(60, result.Tools.CacheTtlSeconds);
        Assert.Equal(5003, result.Api.Port); // C# property default
        Assert.Equal("0.0.0.0", result.Api.Host);
        Assert.Equal(string.Empty, result.Api.ApiKey);
        Assert.Equal(string.Empty, result.Tools.BraveSearch.ApiKey);
    }

    // ────────────────────────────────────────────────────────────────
    // appsettings.json-style in-memory values are bound
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_AppsettingsValues_ArePopulated()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cycle:MinDurationMinutes"] = "30",
            ["Cycle:MaxDurationMinutes"] = "720",
            ["Cycle:ScheduleIntervalMinutes"] = "180",
            ["Api:Port"] = "5003",
            ["Api:Host"] = "0.0.0.0",
            ["Tools:DefaultTimeoutSeconds"] = "45",
            ["Tools:CacheTtlSeconds"] = "120",
            ["Tools:BraveSearch:ApiKey"] = "from_appsettings",
            ["Tools:MarketDataService:BaseUrl"] = "http://market:5002",
        });

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(30, result.Cycle.MinDurationMinutes);
        Assert.Equal(720, result.Cycle.MaxDurationMinutes);
        Assert.Equal(180, result.Cycle.ScheduleIntervalMinutes);
        Assert.Equal(5003, result.Api.Port);
        Assert.Equal(45, result.Tools.DefaultTimeoutSeconds);
        Assert.Equal("from_appsettings", result.Tools.BraveSearch.ApiKey);
        Assert.Equal("http://market:5002", result.Tools.MarketDataService.BaseUrl);
    }

    [Fact]
    public void Bind_AgentSettings_ArePopulated()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Agents:Plan:Model"] = "gpt-4",
            ["Agents:Plan:Temperature"] = "0.5",
            ["Agents:Plan:MaxTokens"] = "2048",
            ["Agents:Plan:TimeoutMinutes"] = "15",
            ["Agents:Plan:MaxRetries"] = "2",
        });

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal("gpt-4", result.Agents.Plan.Model);
        Assert.Equal(0.5, result.Agents.Plan.Temperature);
        Assert.Equal(2048, result.Agents.Plan.MaxTokens);
        Assert.Equal(15, result.Agents.Plan.TimeoutMinutes);
        Assert.Equal(2, result.Agents.Plan.MaxRetries);
    }

    // ────────────────────────────────────────────────────────────────
    // Environment variables (__ convention) override lower-priority sources
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_EnvVarOverridesAppsettings_ForApiKey()
    {
        // Layer 1: base values (simulating appsettings.json)
        var baseLayer = new Dictionary<string, string?>
        {
            ["Api:ApiKey"] = "base_key",
            ["Tools:BraveSearch:ApiKey"] = "base_brave",
        };

        // Layer 2: env vars (simulating ASPNETCORE env var injection).
        // ConfigurationBuilder processes sources in order: last added wins.
        // Note: AddInMemoryCollection uses ':' as hierarchy separator.
        // The actual __ -> : translation only happens with AddEnvironmentVariables().
        var envLayer = new Dictionary<string, string?>
        {
            ["Api:ApiKey"] = "env_key",
            ["Tools:BraveSearch:ApiKey"] = "env_brave",
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(baseLayer)
            .AddInMemoryCollection(envLayer)   // higher priority — added last
            .Build();

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal("env_key", result.Api.ApiKey);
        Assert.Equal("env_brave", result.Tools.BraveSearch.ApiKey);
    }

    [Fact]
    public void Bind_EnvVarOverridesAppsettings_ForCycleSettings()
    {
        var baseLayer = new Dictionary<string, string?>
        {
            ["Cycle:ScheduleIntervalMinutes"] = "360",
        };
        var envLayer = new Dictionary<string, string?>
        {
            ["Cycle:ScheduleIntervalMinutes"] = "120",
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(baseLayer)
            .AddInMemoryCollection(envLayer)
            .Build();

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(120, result.Cycle.ScheduleIntervalMinutes);
    }

    [Fact]
    public void Bind_EnvVarOverridesAppsettings_ForToolUrls()
    {
        var baseLayer = new Dictionary<string, string?>
        {
            ["Tools:MarketDataService:BaseUrl"] = "http://localhost:5002",
            ["Tools:ExecutionService:BaseUrl"] = "http://localhost:5000",
        };
        var envLayer = new Dictionary<string, string?>
        {
            ["Tools:MarketDataService:BaseUrl"] = "http://market-data-service:5002",
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(baseLayer)
            .AddInMemoryCollection(envLayer)
            .Build();

        var result = config.Get<AgentRunnerConfig>()!;

        // Overridden by env var
        Assert.Equal("http://market-data-service:5002", result.Tools.MarketDataService.BaseUrl);
        // Not overridden — base value preserved
        Assert.Equal("http://localhost:5000", result.Tools.ExecutionService.BaseUrl);
    }

    [Fact]
    public void Bind_OnlyBaseLayer_WhenNoEnvOverride_RetainsBaseValues()
    {
        var baseLayer = new Dictionary<string, string?>
        {
            ["Api:ApiKey"] = "only_base",
        };

        var config = BuildConfig(baseLayer);

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal("only_base", result.Api.ApiKey);
    }

    // ────────────────────────────────────────────────────────────────
    // Absent secrets default to empty string (not null)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_MissingSecrets_DefaultToEmptyString()
    {
        // No Api:ApiKey or Tools:BraveSearch:ApiKey provided at all.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Api:Port"] = "5003",
        });

        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal(string.Empty, result.Api.ApiKey);
        Assert.Equal(string.Empty, result.Tools.BraveSearch.ApiKey);
    }

    // ────────────────────────────────────────────────────────────────
    // Ollama configuration binding
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_EmptyConfiguration_OllamaDefaults()
    {
        var config = BuildConfig();
        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal("http://localhost:11434", result.Ollama.BaseUrl);
        Assert.Equal(300, result.Ollama.TimeoutSeconds);
    }

    [Fact]
    public void Bind_OllamaSection_OverridesDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://ollama-host:11434",
            ["Ollama:TimeoutSeconds"] = "600",
        });

        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal("http://ollama-host:11434", result.Ollama.BaseUrl);
        Assert.Equal(600, result.Ollama.TimeoutSeconds);
    }

    // ────────────────────────────────────────────────────────────────
    // MaxIterations — per-agent iteration limit
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_EmptyConfiguration_AgentMaxIterationsDefaultsTo50()
    {
        var config = BuildConfig();
        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal(50, result.Agents.Plan.MaxIterations);
        Assert.Equal(50, result.Agents.Research.MaxIterations);
        Assert.Equal(50, result.Agents.Analyze.MaxIterations);
        Assert.Equal(50, result.Agents.Synthesis.MaxIterations);
        Assert.Equal(50, result.Agents.Evaluation.MaxIterations);
    }

    [Fact]
    public void Bind_AgentMaxIterations_BindsFromConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Agents:Plan:MaxIterations"]     = "20",
            ["Agents:Research:MaxIterations"] = "30",
            ["Agents:Synthesis:MaxIterations"] = "10",
        });

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(20, result.Agents.Plan.MaxIterations);
        Assert.Equal(30, result.Agents.Research.MaxIterations);
        Assert.Equal(10, result.Agents.Synthesis.MaxIterations);
        // Unset agents retain the property default
        Assert.Equal(50, result.Agents.Analyze.MaxIterations);
        Assert.Equal(50, result.Agents.Evaluation.MaxIterations);
    }

    [Fact]
    public void Bind_AgentMaxIterations_EnvVarOverridesAppsettings()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:Plan:MaxIterations"] = "50",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:Plan:MaxIterations"] = "15",
            })
            .Build();

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(15, result.Agents.Plan.MaxIterations);
    }

    // ────────────────────────────────────────────────────────────────
    // Tool retry configuration
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_EmptyConfiguration_ToolRetryDefaults()
    {
        var config = BuildConfig();
        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        Assert.Equal(3, result.Tools.MaxRetries);
        Assert.Equal(30, result.Tools.MaxRetryDelaySeconds);
    }

    [Fact]
    public void Bind_ToolRetrySettings_BindFromConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Tools:MaxRetries"]           = "5",
            ["Tools:MaxRetryDelaySeconds"] = "60",
        });

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(5, result.Tools.MaxRetries);
        Assert.Equal(60, result.Tools.MaxRetryDelaySeconds);
    }

    [Fact]
    public void Bind_ToolRetrySettings_EnvVarOverridesAppsettings()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tools:MaxRetries"]           = "3",
                ["Tools:MaxRetryDelaySeconds"] = "30",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tools:MaxRetries"]           = "7",
                ["Tools:MaxRetryDelaySeconds"] = "45",
            })
            .Build();

        var result = config.Get<AgentRunnerConfig>()!;

        Assert.Equal(7, result.Tools.MaxRetries);
        Assert.Equal(45, result.Tools.MaxRetryDelaySeconds);
    }

    [Fact]
    public void Bind_OllamaEnvVars_OverrideAppsettings()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://from-appsettings:11434",
                ["Ollama:TimeoutSeconds"] = "300",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Simulates env var Ollama__BaseUrl using __ flattened to : by AddEnvironmentVariables
                ["Ollama:BaseUrl"] = "http://from-env:11434",
            })
            .Build();

        var result = config.Get<AgentRunnerConfig>() ?? new AgentRunnerConfig();

        // Second AddInMemoryCollection wins (higher precedence in this builder)
        Assert.Equal("http://from-env:11434", result.Ollama.BaseUrl);
    }

    // ────────────────────────────────────────────────────────────────
    // Helper
    // ────────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
}
