using AgentRunner.Abstractions;
using AgentRunner.Domain.Events;

namespace AgentRunner.Cli;

/// <summary>
/// <see cref="IAgentEventSink"/> implementation that prints structured events to the console.
/// Used in CLI / single-run mode where SignalR is not available.
/// </summary>
public sealed class ConsoleAgentEventSink : IAgentEventSink
{
    public void Publish(AgentEvent evt)
    {
        switch (evt)
        {
            case StepStartedEvent e:
                Console.WriteLine($"[STEP START ] {e.StepName}  cycle={e.CycleId}");
                break;

            case StepCompletedEvent e:
                var status = e.Success ? "OK" : $"FAIL: {e.ErrorMessage}";
                Console.WriteLine($"[STEP DONE  ] {e.StepName}  {e.Duration.TotalSeconds:F1}s  {status}");
                break;

            case LoopStateChangedEvent e:
                Console.WriteLine($"[STATE      ] {e.State}");
                break;

            case CycleCompletedEvent e:
                Console.WriteLine($"[CYCLE DONE ] cycle={e.CycleId}  at={e.CompletedAt:u}");
                break;

            case LoopErrorEvent e:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR      ] {e.Message}");
                Console.ResetColor();
                break;

            case LoopHealthEvent e:
                var color = e.IsCritical ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.ForegroundColor = color;
                Console.WriteLine($"[HEALTH     ] {e.Message}  state={e.State}");
                Console.ResetColor();
                break;

            case TokenReceivedEvent e:
                Console.Write(e.Token);
                break;

            case ToolCallStartedEvent e:
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[TOOL →     ] {e.ToolName}  {e.InputJson}");
                Console.ResetColor();
                break;

            case ToolCallCompletedEvent e:
                Console.ForegroundColor = e.IsError ? ConsoleColor.Red : ConsoleColor.Cyan;
                Console.WriteLine($"[TOOL ←     ] {e.ToolName}  {e.Duration.TotalMilliseconds:F0}ms  {(e.IsError ? "ERROR" : "OK")}");
                Console.ResetColor();
                break;
        }
    }
}
