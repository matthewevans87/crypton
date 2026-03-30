namespace Crypton.Tools;

/// <summary>A message deposited into an agent's mailbox.</summary>
public sealed record MailboxMessage(
    string FromAgent,
    string ToAgent,
    string Content,
    DateTimeOffset Timestamp);
