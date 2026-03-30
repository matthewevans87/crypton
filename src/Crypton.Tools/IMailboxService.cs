namespace Crypton.Tools;

/// <summary>Per-agent mailbox — delivers short messages between agents in the same cycle.</summary>
public interface IMailboxService
{
    IReadOnlyList<MailboxMessage> GetMessages(string agentName, int maxCount);
    void Send(MailboxMessage message);
}
