using System.ComponentModel;
using Microsoft.Extensions.AI;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Sends a message to another agent's mailbox.</summary>
public sealed class SendMailTool : IAgentTool
{
    private readonly IMailboxService _mailbox;

    public string Name => "send_mail";
    public string Description => "Sends a short message to another agent's mailbox. Use to pass findings, requests, or flags to agents by name.";

    // Valid agent names (lowercase) that can receive mail
    private static readonly HashSet<string> ValidAgents =
        new(StringComparer.OrdinalIgnoreCase) { "plan", "research", "analysis", "synthesis", "evaluation" };

    public ToolDocumentation Documentation { get; } = new(
        LongDescription: "Send a short message to another agent's mailbox. Use to surface a critical finding, flag a risk, or request that a downstream agent focus on a specific area. Messages are delivered before that agent's next invocation.",
        Parameters:
        [
            new("to",      "string", Required: true,  Default: null, "The recipient agent. One of: `plan`, `research`, `analysis`, `synthesis`, `evaluation`."),
            new("from",    "string", Required: true,  Default: null, "Your agent name (the sender)."),
            new("message", "string", Required: true,  Default: null, "The message body. Keep it 1–2 sentences. Be specific and actionable."),
        ],
        Returns: "Confirmation string `\"Message delivered to {to}.\"` on success, or an error string if the recipient is invalid.",
        Examples:
        [
            "{\"to\": \"analysis\", \"from\": \"research\", \"message\": \"BTC funding rates are at 6-month highs — watch for long liquidations.\"}",
            "{\"to\": \"plan\", \"from\": \"evaluation\", \"message\": \"Last strategy underperformed: stop was too tight. Recommend widening ATR multiplier next cycle.\"}",
        ],
        Notes:
        [
            "Use sparingly — only when you have a finding that materially affects a downstream agent's decision.",
            "Messages are capped at 5 per inbox. If the inbox is full, the oldest message is dropped.",
            "Do not use this to repeat information already in your output artifact. Only send novel, high-priority signals.",
        ]);

    public SendMailTool(IMailboxService mailbox)
    {
        _mailbox = mailbox;
    }

    public AIFunction AsAIFunction() => AIFunctionFactory.Create(ExecuteCoreAsync, Name);

    public Task<string> InvokeAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
            return Task.FromResult("Error: 'to' is required.");

        if (!parameters.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from))
            return Task.FromResult("Error: 'from' is required.");

        if (!parameters.TryGetValue("message", out var message) || string.IsNullOrWhiteSpace(message))
            return Task.FromResult("Error: 'message' is required.");

        return ExecuteCoreAsync(to, from, message, ct);
    }

    [Description("Delivers a message to another agent's mailbox. Returns a confirmation or error.")]
    internal Task<string> ExecuteCoreAsync(
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

    // ─── Spectre.Console Command ─────────────────────────────────────────────

    public sealed class Command(SendMailTool tool) : AsyncCommand<Command.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandOption("--to <TO>")]
            public string To { get; init; } = string.Empty;

            [CommandOption("--from <FROM>")]
            public string From { get; init; } = string.Empty;

            [CommandOption("--message <MESSAGE>")]
            public string Message { get; init; } = string.Empty;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var result = await tool.ExecuteCoreAsync(settings.To, settings.From, settings.Message);
            Console.WriteLine(result);
            return 0;
        }
    }
}
