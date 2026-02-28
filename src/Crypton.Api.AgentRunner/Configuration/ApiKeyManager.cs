namespace AgentRunner.Configuration;

/// <summary>
/// Obsolete: API keys are now injected via IConfiguration environment variable bindings.
/// This file is retained only to avoid merge conflicts and will be removed.
/// </summary>
[Obsolete("API keys are bound from IConfiguration environment variables instead.")]
public class ApiKeyManager
{
    public void RegisterApiKey(string name, string envVarName) { }
    public string? GetApiKey(string name) => null;
    public void StartAutoReload(TimeSpan interval) { }
    public void StopAutoReload() { }
    public bool ValidateApiKeys() => true;
}
