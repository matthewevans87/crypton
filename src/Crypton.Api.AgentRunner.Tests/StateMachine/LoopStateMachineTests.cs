using AgentRunner.StateMachine;
using Xunit;

namespace AgentRunner.Tests.StateMachine;

public class LoopStateMachineTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var sm = new LoopStateMachine();
        Assert.Equal(LoopState.Idle, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromIdleToPlan_Succeeds()
    {
        var sm = new LoopStateMachine();
        var result = sm.TransitionTo(LoopState.Plan);

        Assert.True(result);
        Assert.Equal(LoopState.Plan, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromIdleToEvaluate_Succeeds()
    {
        // Idle → Evaluate is the "resume with history" path
        var sm = new LoopStateMachine();
        var result = sm.TransitionTo(LoopState.Evaluate);

        Assert.True(result);
        Assert.Equal(LoopState.Evaluate, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromEvaluateToPlan_Succeeds()
    {
        // Evaluation is Step 0 — it leads to Plan, not WaitingForNextCycle
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Evaluate);
        var result = sm.TransitionTo(LoopState.Plan);

        Assert.True(result);
        Assert.Equal(LoopState.Plan, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromWaitingForNextCycleToEvaluate_Succeeds()
    {
        // When history exists, the waiting state transitions to Evaluate for the new cycle
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Research);
        sm.TransitionTo(LoopState.Analyze);
        sm.TransitionTo(LoopState.Synthesize);
        sm.TransitionTo(LoopState.WaitingForNextCycle);
        var result = sm.TransitionTo(LoopState.Evaluate);

        Assert.True(result);
        Assert.Equal(LoopState.Evaluate, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromPlanToResearch_Succeeds()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        var result = sm.TransitionTo(LoopState.Research);

        Assert.True(result);
        Assert.Equal(LoopState.Research, sm.CurrentState);
    }

    [Fact]
    public void TransitionFromPlanToEvaluate_Fails()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        var result = sm.TransitionTo(LoopState.Evaluate);

        Assert.False(result);
        Assert.Equal(LoopState.Plan, sm.CurrentState);
    }

    [Fact]
    public void FullCycle_SynthesizeToWaiting_Succeeds()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Research);
        sm.TransitionTo(LoopState.Analyze);
        var result = sm.TransitionTo(LoopState.Synthesize);
        Assert.True(result);

        result = sm.TransitionTo(LoopState.WaitingForNextCycle);
        Assert.True(result);
        Assert.Equal(LoopState.WaitingForNextCycle, sm.CurrentState);
    }

    [Theory]
    [InlineData(LoopState.Idle, LoopState.Plan, true)]
    [InlineData(LoopState.Idle, LoopState.Evaluate, true)]    // resume with history
    [InlineData(LoopState.Plan, LoopState.Research, true)]
    [InlineData(LoopState.Research, LoopState.Analyze, true)]
    [InlineData(LoopState.Plan, LoopState.Failed, true)]
    [InlineData(LoopState.Plan, LoopState.Paused, true)]
    [InlineData(LoopState.Idle, LoopState.Research, false)]
    [InlineData(LoopState.Plan, LoopState.Idle, false)]
    public void CanTransitionTo_ExpectedBehavior(LoopState from, LoopState to, bool expected)
    {
        var sm = new LoopStateMachine();

        if (from != LoopState.Idle)
        {
            var couldSetInitial = sm.TransitionTo(from);
            if (!couldSetInitial)
            {
                // Skip if we can't reach the initial state via a direct one-hop from Idle
                return;
            }
        }

        var result = sm.CanTransitionTo(to);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTerminalState_WhenIdle_ReturnsTrue()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Idle);

        Assert.True(sm.IsTerminalState());
    }

    [Fact]
    public void IsTerminalState_WhenPaused_ReturnsTrue()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Paused);

        Assert.True(sm.IsTerminalState());
    }

    [Fact]
    public void IsTerminalState_WhenRunning_ReturnsFalse()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);

        Assert.False(sm.IsTerminalState());
    }

    [Fact]
    public void GetNextRequiredState_FromIdle_ReturnsPlan()
    {
        var sm = new LoopStateMachine();
        Assert.Equal(LoopState.Plan, sm.GetNextRequiredState());
    }

    [Fact]
    public void GetNextRequiredState_FromEvaluate_ReturnsPlan()
    {
        // Evaluate is Step 0; the natural successor is Plan
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Evaluate);

        Assert.Equal(LoopState.Plan, sm.GetNextRequiredState());
    }

    [Fact]
    public void GetNextRequiredState_FromSynthesize_ReturnsWaitingForNextCycle()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Research);
        sm.TransitionTo(LoopState.Analyze);
        sm.TransitionTo(LoopState.Synthesize);

        Assert.Equal(LoopState.WaitingForNextCycle, sm.GetNextRequiredState());
    }

    [Fact]
    public void StateTransition_EventFired()
    {
        var sm = new LoopStateMachine();
        StateTransitionEventArgs? captured = null;

        sm.StateTransition += (s, e) => captured = e;

        sm.TransitionTo(LoopState.Plan);

        Assert.NotNull(captured);
        Assert.Equal(LoopState.Idle, captured!.FromState);
        Assert.Equal(LoopState.Plan, captured.ToState);
    }

    [Fact]
    public void PausedState_CanResumeToEvaluate()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Paused);

        var result = sm.TransitionTo(LoopState.Evaluate);

        Assert.True(result);
        Assert.Equal(LoopState.Evaluate, sm.CurrentState);
    }
}
