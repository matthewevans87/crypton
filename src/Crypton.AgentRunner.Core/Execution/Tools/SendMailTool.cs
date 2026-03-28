using System.ComponentModel;
using AgentRunner.Abstractions;
using AgentRunner.Domain;
using Microsoft.Extensions.AI;

namespace AgentRunner.Execution.Tools;

/// <summary>Sends a message to another agent's mailbox.</summary>
public sealed class SendMailTool : IAgentTool
{
    private readonly IMailboxService _mailbox;

    public string Name => "send_mail";
    public string Description => "Sends a short message to another agent's mailbox. Use to pass findings, requests, or flags to agents by name.";

    // Valid agent names (lowercase) that can receive mail
    private static readonly HashSet<string> ValidAgents =
        new(StringComparer.OrdinalIgnoreCase) { "plan", "research", "analysis", "synthesis", "evaluation" };

    public SendMailTool(IMailboxService mailbox)
    {
        _mailbox = mailbox;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteAsync, Name);

    [Description("Delivers a message to another agent's mailbox. Returns a confirmation or error.")]
    private Task<string> ExecuteAsync(
        [Description("The agent to send the message to. One of: plan, research, analysis, synthesis, evaluation.")] string to,
        [Description("The name of the sending agent.")] string from,
        [Description("The message body. Keep it 1–2 sentences.")] string message,
        CancellationToken cancellationToken = default)
    {
        if (!ValidAgents.Contains(to))
            return Task.FromResult($"Error: Unknown recipient '{to}'. Valid agents: {string.Join(", ", ValidAgents)}.");

        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult("Error: Message body must not be empty.");

        var mail = new MailboxMessage(
            FromAgent: from,
            ToAgent: to.ToLowerInvariant(),
            Content: message.Trim(),
            Timestamp: DateTimeOffset.UtcNow);

        _mailbox.Send(mail);
        return Task.FromResult($"Message delivered to {to}.");
    }
}
