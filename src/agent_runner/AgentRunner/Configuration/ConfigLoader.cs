using AgentRunner.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentRunner.Configuration;

public class ConfigLoader
{
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private AgentRunnerConfig? _cachedConfig;
    private DateTime _lastLoaded = DateTime.MinValue;
    private readonly object _lock = new();
    private readonly TimeSpan _reloadDebounce = TimeSpan.FromSeconds(2);

    public event EventHandler<AgentRunnerConfig>? ConfigChanged;

    public ConfigLoader(string configPath = "config.yaml")
    {
        _configPath = configPath;
    }

    public AgentRunnerConfig Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_configPath))
            {
                _cachedConfig = CreateDefaultConfig();
                return _cachedConfig;
            }

            var fileInfo = new FileInfo(_configPath);
            if (fileInfo.LastWriteTime <= _lastLoaded && _cachedConfig != null)
            {
                return _cachedConfig;
            }

            var yaml = File.ReadAllText(_configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            _cachedConfig = deserializer.Deserialize<AgentRunnerConfig>(yaml) ?? CreateDefaultConfig();
            _lastLoaded = DateTime.UtcNow;
            
            return _cachedConfig;
        }
    }

    public void StartWatching()
    {
        if (_watcher != null) return;

        var directory = Path.GetDirectoryName(_configPath);
        var fileName = Path.GetFileName(_configPath);

        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnConfigFileChanged;
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        Task.Run(async () =>
        {
            await Task.Delay(_reloadDebounce);

            try
            {
                var oldConfig = _cachedConfig;
                var newConfig = Load();

                if (oldConfig != null && newConfig != oldConfig)
                {
                    ConfigChanged?.Invoke(this, newConfig);
                }
            }
            catch (Exception)
            {
            }
        });
    }

    public void Save(AgentRunnerConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(config);
        
        lock (_lock)
        {
            File.WriteAllText(_configPath, yaml);
            _cachedConfig = config;
            _lastLoaded = DateTime.UtcNow;
        }
    }

    private static AgentRunnerConfig CreateDefaultConfig()
    {
        return new AgentRunnerConfig
        {
            Cycle = new CycleConfig
            {
                MinDurationMinutes = 60,
                MaxDurationMinutes = 1440,
                ScheduleIntervalMinutes = 360,
                EnableParallelExecution = false
            },
            Agents = new AgentConfig
            {
                Plan = new AgentSettings { TimeoutMinutes = 30, MaxRetries = 3 },
                Research = new AgentSettings { TimeoutMinutes = 45, MaxRetries = 3 },
                Analyze = new AgentSettings { TimeoutMinutes = 30, MaxRetries = 3 },
                Synthesis = new AgentSettings { TimeoutMinutes = 15, MaxRetries = 3 },
                Evaluation = new AgentSettings { TimeoutMinutes = 30, MaxRetries = 3 }
            },
            Tools = new ToolConfig
            {
                DefaultTimeoutSeconds = 30,
                CacheTtlSeconds = 60,
                ExecutionService = new ExecutionServiceConfig
                {
                    BaseUrl = "http://localhost:5000"
                }
            },
            Storage = new StorageConfig
            {
                BasePath = "./artifacts",
                MaxMailboxMessages = 5,
                ArchiveRetentionCount = 30
            },
            Api = new ApiConfig
            {
                Host = "0.0.0.0",
                Port = 8080
            },
            Logging = new LoggingConfig
            {
                Level = "Information",
                OutputPath = "./logs"
            }
        };
    }
}
