using AgentRunner.Domain;
using AgentRunner.Orchestration;
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
        Assert.True(sm.TransitionTo(LoopState.Plan));
        Assert.Equal(LoopState.Plan, sm.CurrentState);
    }

    [Theory]
    [InlineData(LoopState.Idle, LoopState.Plan, true)]
    [InlineData(LoopState.Idle, LoopState.Research, false)]
    [InlineData(LoopState.Plan, LoopState.Research, true)]
    [InlineData(LoopState.Plan, LoopState.Evaluate, false)]
    [InlineData(LoopState.Research, LoopState.Analyze, true)]
    [InlineData(LoopState.Analyze, LoopState.Synthesize, true)]
    [InlineData(LoopState.Synthesize, LoopState.WaitingForNextCycle, true)]
    [InlineData(LoopState.WaitingForNextCycle, LoopState.Evaluate, true)]
    [InlineData(LoopState.Evaluate, LoopState.Plan, true)]
    public void CanTransitionTo_ReturnsExpected(LoopState from, LoopState to, bool expected)
    {
        var sm = new LoopStateMachine();
        // Reach the 'from' state via valid transition chain
        foreach (var step in GetTransitionChain(from))
            sm.TransitionTo(step);
        Assert.Equal(expected, sm.CanTransitionTo(to));
    }

    // Returns the sequence of transitions needed to reach 'target' from Idle
    private static LoopState[] GetTransitionChain(LoopState target) => target switch
    {
        LoopState.Idle => [],
        LoopState.Plan => [LoopState.Plan],
        LoopState.Research => [LoopState.Plan, LoopState.Research],
        LoopState.Analyze => [LoopState.Plan, LoopState.Research, LoopState.Analyze],
        LoopState.Synthesize => [LoopState.Plan, LoopState.Research, LoopState.Analyze, LoopState.Synthesize],
        LoopState.WaitingForNextCycle => [LoopState.Plan, LoopState.Research, LoopState.Analyze, LoopState.Synthesize, LoopState.WaitingForNextCycle],
        LoopState.Evaluate => [LoopState.Plan, LoopState.Research, LoopState.Analyze, LoopState.Synthesize, LoopState.WaitingForNextCycle, LoopState.Evaluate],
        _ => [target]
    };

    [Fact]
    public void TransitionTo_InvalidTransition_ReturnsFalse()
    {
        var sm = new LoopStateMachine();
        Assert.False(sm.TransitionTo(LoopState.Research));
    }

    [Fact]
    public void StateTransition_EventFires_OnValidTransition()
    {
        var sm = new LoopStateMachine();
        LoopState? from = null;
        LoopState? to = null;
        sm.StateTransition += (_, args) =>
        {
            from = args.From;
            to = args.To;
        };
        sm.TransitionTo(LoopState.Plan);
        Assert.Equal(LoopState.Idle, from);
        Assert.Equal(LoopState.Plan, to);
    }

    [Fact]
    public void StateEntered_EventFires_OnValidTransition()
    {
        var sm = new LoopStateMachine();
        LoopState? entered = null;
        sm.StateEntered += (_, state) => entered = state;
        sm.TransitionTo(LoopState.Plan);
        Assert.Equal(LoopState.Plan, entered);
    }

    [Fact]
    public void AnyState_CanTransitionTo_Paused()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        Assert.True(sm.CanTransitionTo(LoopState.Paused));
    }

    [Fact]
    public void AnyState_CanTransitionTo_Failed()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        Assert.True(sm.CanTransitionTo(LoopState.Failed));
    }

    [Fact]
    public void Failed_CanTransitionTo_Idle()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Failed);
        Assert.True(sm.CanTransitionTo(LoopState.Idle));
    }
}
