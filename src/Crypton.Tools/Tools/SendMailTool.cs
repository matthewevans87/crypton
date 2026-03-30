using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Crypton.Tools.Tools;

/// <summary>Sends a short message to another agent's mailbox.</summary>
[Description("Send a short message to another agent's mailbox. Use to surface a critical finding or flag a risk to a downstream agent.")]
public sealed class SendMailTool : AsyncCommand<SendMailTool.Settings>
{
    private static readonly HashSet<string> ValidAgents =
        new(StringComparer.OrdinalIgnoreCase) { "plan", "research", "analysis", "synthesis", "evaluation" };

    private readonly IMailboxService _mailbox;
    private readonly IAnsiConsole _console;

    public SendMailTool(IMailboxService mailbox, IAnsiConsole? console = null)
    {
        _mailbox = mailbox;
        _console = console ?? AnsiConsole.Console;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--to <TO>")]
        [Description("Recipient agent: plan, research, analysis, synthesis, or evaluation.")]
        public required string To { get; init; }

        [CommandOption("--from <FROM>")]
        [Description("Sending agent name.")]
        public required string From { get; init; }

        [CommandOption("--message <MESSAGE>")]
        [Description("Message body. Keep it 1-2 sentences and actionable.")]
        public required string Message { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!ValidAgents.Contains(settings.To))
        {
            _console.WriteLine($"Error: Unknown recipient '{settings.To}'. Valid agents: {string.Join(", ", ValidAgents)}.");
            return Task.FromResult(1);
        }

        if (string.IsNullOrWhiteSpace(settings.Message))
        {
            _console.WriteLine("Error: Message body must not be empty.");
            return Task.FromResult(1);
        }

        _mailbox.Send(new MailboxMessage(
            FromAgent: settings.From,
            ToAgent: settings.To.ToLowerInvariant(),
            Content: settings.Message.Trim(),
            Timestamp: DateTimeOffset.UtcNow));

        _console.WriteLine($"Message delivered to {settings.To}.");
        return Task.FromResult(0);
    }
}
