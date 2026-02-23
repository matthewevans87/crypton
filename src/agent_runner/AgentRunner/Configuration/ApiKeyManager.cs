namespace AgentRunner.Configuration;

public class ApiKeyManager
{
    private readonly Dictionary<string, string> _apiKeys = new();
    private readonly Dictionary<string, string> _envVarNames = new();
    private readonly object _lock = new();
    private Timer? _reloadTimer;

    public ApiKeyManager()
    {
        RegisterApiKey("brave_search", "BRAVE_SEARCH_API_KEY");
    }

    public void RegisterApiKey(string name, string envVarName)
    {
        lock (_lock)
        {
            _envVarNames[name] = envVarName;
            ReloadKey(name);
        }
    }

    public string? GetApiKey(string name)
    {
        lock (_lock)
        {
            return _apiKeys.TryGetValue(name, out var key) ? key : null;
        }
    }

    public void StartAutoReload(TimeSpan interval)
    {
        _reloadTimer = new Timer(_ => ReloadAllKeys(), null, interval, interval);
    }

    public void StopAutoReload()
    {
        _reloadTimer?.Dispose();
        _reloadTimer = null;
    }

    private void ReloadAllKeys()
    {
        lock (_lock)
        {
            foreach (var name in _envVarNames.Keys.ToList())
            {
                ReloadKey(name);
            }
        }
    }

    private void ReloadKey(string name)
    {
        if (!_envVarNames.TryGetValue(name, out var envVarName))
            return;

        var newKey = Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;
        
        if (_apiKeys.TryGetValue(name, out var existingKey) && existingKey != newKey)
        {
            _apiKeys[name] = newKey;
        }
        else if (!_apiKeys.ContainsKey(name))
        {
            _apiKeys[name] = newKey;
        }
    }

    public bool ValidateApiKeys()
    {
        lock (_lock)
        {
            foreach (var (name, envVar) in _envVarNames)
            {
                var key = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrEmpty(key))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
