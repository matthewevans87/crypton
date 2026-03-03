namespace AgentRunner.Agents;

/// <summary>Event args for when an agent step begins executing.</summary>
public sealed class StepStartedEventArgs : EventArgs
{
    public required string StepName { get; init; }
    public required string? CycleId { get; init; }
    public required DateTime StartedAt { get; init; }
}

/// <summary>Event args for when an agent step finishes (success, failure, or timeout).</summary>
public sealed class StepCompletedEventArgs : EventArgs
{
    public required string StepName { get; init; }
    public required string? CycleId { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required TimeSpan Duration { get; init; }
    public required DateTime CompletedAt { get; init; }
}

/// <summary>Event args for a single streaming LLM token.</summary>
public sealed class TokenEventArgs : EventArgs
{
    public required string Token { get; init; }
    public required string StepName { get; init; }
}

/// <summary>Event args for a structured agent event (tool call, iteration marker, etc.).</summary>
public sealed class AgentEventArgs : EventArgs
{
    public required string EventMessage { get; init; }
    public required string StepName { get; init; }
}
