namespace AgentRunner.Configuration;

/// <summary>
/// Obsolete: configuration is now loaded via IConfiguration / appsettings.json.
/// This file is retained only to avoid merge conflicts and will be removed.
/// </summary>
[Obsolete("Use IConfiguration (WebApplication.CreateBuilder) instead.")]
public class ConfigLoader
{
    public ConfigLoader(string configPath = "config.yaml") { }
    public AgentRunnerConfig Load() => new();
    public void StartWatching() { }
    public void StopWatching() { }
}
