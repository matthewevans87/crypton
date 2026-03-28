using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;

namespace AgentRunner.Infrastructure;

/// <summary>
/// File-based mailbox service. Each agent has its own JSON file under the mailboxes directory.
/// Enforces the max-messages-per-inbox constraint by dropping the oldest messages first.
/// </summary>
public sealed class FileMailboxService : IMailboxService
{
    private readonly string _mailboxesPath;
    private readonly int _maxMessages;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FileMailboxService(StorageConfig config)
    {
        _mailboxesPath = Path.GetFullPath(config.MailboxesPath);
        _maxMessages = config.MaxMailboxMessages;
        Directory.CreateDirectory(_mailboxesPath);
    }

    public IReadOnlyList<MailboxMessage> GetMessages(string agentName, int maxCount)
    {
        lock (_lock)
        {
            return Load(agentName)
                .OrderByDescending(m => m.Timestamp)
                .Take(maxCount)
                .ToList();
        }
    }

    public void Send(MailboxMessage message)
    {
        lock (_lock)
        {
            var messages = Load(message.ToAgent);
            messages.Add(message);

            if (messages.Count > _maxMessages)
                messages = messages.OrderByDescending(m => m.Timestamp).Take(_maxMessages).ToList();

            Save(message.ToAgent, messages);
        }
    }

    private List<MailboxMessage> Load(string agentName)
    {
        var path = FilePath(agentName);
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<MailboxMessage>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save(string agentName, List<MailboxMessage> messages)
    {
        var json = JsonSerializer.Serialize(messages, JsonOptions);
        var tmp = FilePath(agentName) + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath(agentName), overwrite: true);
    }

    private string FilePath(string agentName) =>
        Path.Combine(_mailboxesPath, $"mailbox.{agentName.ToLowerInvariant()}.json");
}
