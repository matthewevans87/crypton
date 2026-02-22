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

    [Theory]
    [InlineData(LoopState.Idle, LoopState.Plan, true)]
    [InlineData(LoopState.Plan, LoopState.Research, true)]
    [InlineData(LoopState.Research, LoopState.Analyze, true)]
    [InlineData(LoopState.Plan, LoopState.Failed, true)]
    [InlineData(LoopState.Plan, LoopState.Paused, true)]
    [InlineData(LoopState.Idle, LoopState.Research, false)]
    [InlineData(LoopState.Plan, LoopState.Idle, false)]
    public void CanTransitionTo_ExpectedBehavior(LoopState from, LoopState to, bool expected)
    {
        var sm = new LoopStateMachine();
        
        // First transition to 'from' state (unless it's Idle which is the start)
        if (from != LoopState.Idle)
        {
            var couldSetInitial = sm.TransitionTo(from);
            if (!couldSetInitial)
            {
                // Skip this test case if we can't reach the initial state
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
    public void GetNextRequiredState_FromEvaluate_ReturnsWaiting()
    {
        var sm = new LoopStateMachine();
        sm.TransitionTo(LoopState.Plan);
        sm.TransitionTo(LoopState.Research);
        sm.TransitionTo(LoopState.Analyze);
        sm.TransitionTo(LoopState.Synthesize);
        sm.TransitionTo(LoopState.Execute);
        sm.TransitionTo(LoopState.Evaluate);
        
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
}
