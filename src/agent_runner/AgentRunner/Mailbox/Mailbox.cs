namespace AgentRunner.Mailbox;

public class Mailbox
{
    private readonly string _filePath;
    private readonly int _maxMessages;
    private readonly object _lock = new();
    private List<MailboxMessage> _messages = new();

    public string AgentName { get; }

    public Mailbox(string agentName, string mailboxesPath, int maxMessages = 5)
    {
        AgentName = agentName;
        _maxMessages = maxMessages;
        
        if (!Directory.Exists(mailboxesPath))
        {
            Directory.CreateDirectory(mailboxesPath);
        }
        
        _filePath = Path.Combine(mailboxesPath, $"mailbox.{agentName}");
        LoadMessages();
    }

    private void LoadMessages()
    {
        if (!File.Exists(_filePath))
        {
            _messages = new List<MailboxMessage>();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_filePath);
            _messages = lines
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => MailboxMessage.Parse(l))
                .Where(m => m != null)
                .Cast<MailboxMessage>()
                .ToList();
        }
        catch
        {
            _messages = new List<MailboxMessage>();
        }
    }

    public void Deposit(MailboxMessage message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            
            if (_messages.Count > _maxMessages)
            {
                _messages = _messages
                    .OrderByDescending(m => m.Timestamp)
                    .Take(_maxMessages)
                    .ToList();
            }
            
            SaveMessages();
        }
    }

    public List<MailboxMessage> GetMessages(int count = 5)
    {
        lock (_lock)
        {
            return _messages
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    public List<MailboxMessage> GetAllMessages()
    {
        lock (_lock)
        {
            return _messages.ToList();
        }
    }

    private void SaveMessages()
    {
        var lines = _messages.Select(m => m.ToFileLine());
        File.WriteAllLines(_filePath, lines);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }
}

public class MailboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromAgent { get; set; } = string.Empty;
    public string ToAgent { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MessageType Type { get; set; } = MessageType.Forward;

    public static MailboxMessage Parse(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 4)
            return new MailboxMessage { Content = line };

        return new MailboxMessage
        {
            Id = parts[0],
            FromAgent = parts[1],
            ToAgent = parts[2],
            Content = parts[3],
            Timestamp = DateTime.TryParse(parts[4], out var ts) ? ts : DateTime.UtcNow,
            Type = Enum.TryParse<MessageType>(parts[5], out var mt) ? mt : MessageType.Forward
        };
    }

    public string ToFileLine()
    {
        return $"{Id}|{FromAgent}|{ToAgent}|{Content}|{Timestamp:O}|{Type}";
    }
}

public enum MessageType
{
    Forward,
    Feedback,
    Broadcast
}
