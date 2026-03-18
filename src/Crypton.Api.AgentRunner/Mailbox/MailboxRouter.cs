using System.Text.RegularExpressions;
using AgentRunner.Agents;
using AgentRunner.StateMachine;

namespace AgentRunner.Mailbox;

/// <summary>
/// Routes agent output to the appropriate mailboxes after each step completes.
/// Handles forward messages (to the next agent), feedback messages (to the previous agent),
/// and evaluation broadcasts (to all agents).
/// </summary>
public class MailboxRouter
{
    private static readonly Dictionary<LoopState, (string? Forward, string? Backward)> Routing =
        new()
        {
            [LoopState.Plan] = ("research", null),
            [LoopState.Research] = ("analysis", "plan"),
            [LoopState.Analyze] = ("synthesis", "research"),
            [LoopState.Synthesize] = ("evaluation", "analysis"),
            [LoopState.Evaluate] = (null, null),
        };

    private readonly MailboxManager _mailboxManager;

    public MailboxRouter(MailboxManager mailboxManager)
    {
        _mailboxManager = mailboxManager;
    }

    public Task RouteAsync(LoopState state, AgentInvocationResult result)
    {
        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return Task.CompletedTask;

        if (!Routing.TryGetValue(state, out var routing))
            return Task.CompletedTask;

        var (forwardAgent, backwardAgent) = routing;

        if (!string.IsNullOrEmpty(forwardAgent))
        {
            _mailboxManager.Deposit(forwardAgent, new MailboxMessage
            {
                FromAgent = state.ToString(),
                ToAgent = forwardAgent,
                Content = ExtractTaggedContent(result.Output, $"mailbox_to_{forwardAgent}",
                    fallback: "No forward message"),
                Type = MessageType.Forward
            });
        }

        if (!string.IsNullOrEmpty(backwardAgent))
        {
            _mailboxManager.Deposit(backwardAgent, new MailboxMessage
            {
                FromAgent = state.ToString(),
                ToAgent = backwardAgent,
                Content = ExtractTaggedContent(result.Output, "feedback",
                    fallback: "No feedback"),
                Type = MessageType.Feedback
            });
        }

        if (state == LoopState.Evaluate)
        {
            var broadcastContent = ExtractTaggedContent(result.Output, "broadcast",
                fallback: "Broadcast message");
            _mailboxManager.Broadcast("evaluation", broadcastContent);
        }

        return Task.CompletedTask;
    }

    private static string ExtractTaggedContent(string output, string tag, string fallback)
    {
        var match = Regex.Match(output, $@"<{tag}>(.*?)</{tag}>",
            RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : fallback;
    }
}
