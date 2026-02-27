using AgentRunner.Configuration;
using AgentRunner.Mailbox;

namespace AgentRunner.Mailbox;

public class MailboxManager
{
    private readonly Dictionary<string, Mailbox> _mailboxes = new();
    private readonly int _maxMessages;

    public MailboxManager(StorageConfig config)
    {
        _maxMessages = config.MaxMailboxMessages;
        InitializeMailboxes(config.MailboxesPath);
    }

    private void InitializeMailboxes(string mailboxesPath)
    {
        var agents = new[] { "plan", "research", "analysis", "synthesis", "evaluation" };
        
        foreach (var agent in agents)
        {
            _mailboxes[agent] = new Mailbox(agent, mailboxesPath, _maxMessages);
        }
    }

    public Mailbox GetMailbox(string agentName)
    {
        if (!_mailboxes.TryGetValue(agentName.ToLower(), out var mailbox))
        {
            throw new ArgumentException($"Unknown agent: {agentName}");
        }
        return mailbox;
    }

    public void Deposit(string toAgent, MailboxMessage message)
    {
        var mailbox = GetMailbox(toAgent);
        mailbox.Deposit(message);
    }

    public void Broadcast(string fromAgent, string content)
    {
        var message = new MailboxMessage
        {
            FromAgent = fromAgent,
            Content = content,
            Type = MessageType.Broadcast
        };

        foreach (var (agentName, mailbox) in _mailboxes)
        {
            if (agentName != fromAgent)
            {
                message.ToAgent = agentName;
                mailbox.Deposit(message);
            }
        }
    }

    public List<MailboxMessage> GetMessages(string agentName, int count = 5)
    {
        var mailbox = GetMailbox(agentName);
        return mailbox.GetMessages(count);
    }

    public Dictionary<string, List<MailboxMessage>> GetAllMailboxContents()
    {
        return _mailboxes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetAllMessages()
        );
    }

    public void ClearAll()
    {
        foreach (var mailbox in _mailboxes.Values)
        {
            mailbox.Clear();
        }
    }
}
