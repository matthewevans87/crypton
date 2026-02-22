using AgentRunner.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentRunner.Configuration;

public class ConfigLoader
{
    private readonly string _configPath;

    public ConfigLoader(string configPath = "config.yaml")
    {
        _configPath = configPath;
    }

    public AgentRunnerConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return CreateDefaultConfig();
        }

        var yaml = File.ReadAllText(_configPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<AgentRunnerConfig>(yaml) ?? CreateDefaultConfig();
    }

    public void Save(AgentRunnerConfig config)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(config);
        File.WriteAllText(_configPath, yaml);
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
