using AgentRunner.Mailbox;

namespace AgentRunner.Tools;

/// <summary>
/// Allows an agent to send a direct message to another agent's mailbox.
/// </summary>
public class SendMailTool : Tool
{
    private static readonly string[] ValidAgents = ["plan", "research", "analysis", "synthesis", "evaluation"];

    private readonly MailboxManager _mailboxManager;

    public SendMailTool(MailboxManager mailboxManager)
    {
        _mailboxManager = mailboxManager;
    }

    public override string Name => "send_mail";

    public override string Description =>
        "Sends a direct message to another agent's mailbox. Use this to pass instructions, findings, or feedback " +
        "to a specific agent. Valid recipients: plan, research, analysis, synthesis, evaluation.";

    public override ToolParameterSchema? Parameters => new()
    {
        Type = "object",
        Properties = new Dictionary<string, ToolParameterProperty>
        {
            ["to"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Recipient agent name: plan, research, analysis, synthesis, or evaluation."
            },
            ["from"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Sender agent name (the calling agent)."
            },
            ["message"] = new ToolParameterProperty
            {
                Type = "string",
                Description = "Message content (1-2 sentences, actionable and concise)."
            }
        },
        Required = new List<string> { "to", "from", "message" }
    };

    public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var to = parameters.GetString("to")?.ToLowerInvariant();
        var from = parameters.GetString("from")?.ToLowerInvariant();
        var message = parameters.GetString("message");

        if (string.IsNullOrWhiteSpace(to))
            return Task.FromResult(new ToolResult { Success = false, Error = "Missing required parameter 'to'." });

        if (string.IsNullOrWhiteSpace(from))
            return Task.FromResult(new ToolResult { Success = false, Error = "Missing required parameter 'from'." });

        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult(new ToolResult { Success = false, Error = "Missing required parameter 'message'." });

        if (!Array.Exists(ValidAgents, a => a == to))
            return Task.FromResult(new ToolResult
            {
                Success = false,
                Error = $"Unknown recipient '{to}'. Valid agents: {string.Join(", ", ValidAgents)}."
            });

        try
        {
            _mailboxManager.Deposit(to, new MailboxMessage
            {
                FromAgent = from,
                ToAgent = to,
                Content = message,
                Type = MessageType.Forward
            });

            return Task.FromResult(new ToolResult
            {
                Success = true,
                Data = new { to, from, message }
            });
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(new ToolResult { Success = false, Error = ex.Message });
        }
    }
}
