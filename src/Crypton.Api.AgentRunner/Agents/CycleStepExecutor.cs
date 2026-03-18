using AgentRunner.Artifacts;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;

namespace AgentRunner.Agents;

/// <summary>Result returned from a single agent step execution.</summary>
public sealed record StepExecutionResult(
    StepOutcome Outcome,
    string? ErrorMessage,
    DateTime StartTime,
    DateTime EndTime);

/// <summary>
/// Executes a single agent step: builds context, invokes the LLM, validates the artifact,
/// saves it to disk, and routes mailbox messages. All cycle state is passed as explicit
/// parameters — no implicit nullability required by the caller.
/// </summary>
public class CycleStepExecutor
{
    private static readonly Dictionary<LoopState, string> ArtifactNames = new()
    {
        [LoopState.Plan] = "plan.md",
        [LoopState.Research] = "research.md",
        [LoopState.Analyze] = "analysis.md",
        [LoopState.Synthesize] = "strategy.json",
        [LoopState.Evaluate] = "evaluation.md",
    };

    private readonly IAgentContextBuilder _contextBuilder;
    private readonly IAgentInvoker _agentInvoker;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxRouter _mailboxRouter;
    private readonly IEventLogger _logger;

    public CycleStepExecutor(
        IAgentContextBuilder contextBuilder,
        IAgentInvoker agentInvoker,
        ArtifactManager artifactManager,
        MailboxRouter mailboxRouter,
        IEventLogger logger)
    {
        _contextBuilder = contextBuilder;
        _agentInvoker = agentInvoker;
        _artifactManager = artifactManager;
        _mailboxRouter = mailboxRouter;
        _logger = logger;
    }

    public async Task<StepExecutionResult> ExecuteAsync(
        LoopState state,
        CycleContext cycle,
        string? latestPreviousCycleId,
        Action<string>? onToken,
        Action<string>? onAgentEvent,
        CancellationToken ct)
    {
        _logger.LogInfo($"Executing step: {state}");
        var startTime = DateTime.UtcNow;

        try
        {
            var context = _contextBuilder.BuildContext(state, cycle.CycleId, latestPreviousCycleId);
            var result = await _agentInvoker.InvokeAsync(context, ct, onToken, onAgentEvent);

            if (!result.Success)
            {
                _logger.LogError($"Agent {state} failed: {result.Error}");
                return new StepExecutionResult(StepOutcome.Failed, result.Error, startTime, DateTime.UtcNow);
            }

            _logger.LogInfo($"Agent {state} output (first 200 chars): " +
                $"{(result.Output?.Length > 200 ? result.Output[..200] + "..." : result.Output)}");

            var artifactName = ArtifactNames[state];
            var validation = ValidateArtifact(state, result.Output ?? "");

            if (!validation.IsValid)
            {
                var error = $"Artifact validation failed for {artifactName}: " +
                    string.Join("; ", validation.Errors);
                _logger.LogWarning(error);
                return new StepExecutionResult(StepOutcome.Failed, error, startTime, DateTime.UtcNow);
            }

            if (validation.Warnings.Any())
                _logger.LogWarning($"Artifact warnings for {artifactName}: {string.Join(", ", validation.Warnings)}");

            _artifactManager.SaveArtifact(cycle.CycleId, artifactName, result.Output ?? "");
            await _mailboxRouter.RouteAsync(state, result);

            return new StepExecutionResult(StepOutcome.Success, null, startTime, DateTime.UtcNow);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError($"Step {state} timed out");
            return new StepExecutionResult(StepOutcome.Timeout, "Step timed out", startTime, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Step {state} failed: {ex.Message}");
            return new StepExecutionResult(StepOutcome.Failed, ex.Message, startTime, DateTime.UtcNow);
        }
    }

    private ArtifactValidationResult ValidateArtifact(LoopState state, string content)
    {
        try
        {
            var validator = ArtifactValidators.ForState(state);
            return validator.Validate(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating artifact for {state}: {ex.Message}");
            return ArtifactValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }
}
