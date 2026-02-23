using AgentRunner.Agents;
using AgentRunner.StateMachine;
using Prometheus;

namespace AgentRunner.Telemetry;

public class MetricsCollector
{
    private static readonly Counter CycleDurationTotal = Metrics
        .CreateCounter("agent_runner_cycle_duration_seconds", "Total cycle duration in seconds");

    private static readonly Counter CycleDurationStep = Metrics
        .CreateCounter("agent_runner_step_duration_seconds", "Step duration in seconds",
            new CounterConfiguration { LabelNames = new[] { "step" } });

    private static readonly Counter StepSuccessTotal = Metrics
        .CreateCounter("agent_runner_step_success_total", "Total successful step executions",
            new CounterConfiguration { LabelNames = new[] { "step" } });

    private static readonly Counter StepFailureTotal = Metrics
        .CreateCounter("agent_runner_step_failure_total", "Total failed step executions",
            new CounterConfiguration { LabelNames = new[] { "step" } });

    private static readonly Counter RetryTotal = Metrics
        .CreateCounter("agent_runner_retry_total", "Total retry attempts",
            new CounterConfiguration { LabelNames = new[] { "step" } });

    private static readonly Counter ToolExecutionTotal = Metrics
        .CreateCounter("agent_runner_tool_execution_total", "Total tool executions",
            new CounterConfiguration { LabelNames = new[] { "tool", "status" } });

    private static readonly Histogram ToolExecutionDuration = Metrics
        .CreateHistogram("agent_runner_tool_execution_duration_seconds", "Tool execution duration in seconds",
            new HistogramConfiguration
            {
                LabelNames = new[] { "tool" }
            });

    private static readonly Gauge CurrentState = Metrics
        .CreateGauge("agent_runner_current_state", "Current state of the agent runner (numeric)");

    private static readonly Gauge CycleCount = Metrics
        .CreateGauge("agent_runner_cycle_count", "Total number of completed cycles");

    public void RecordStepDuration(string step, double durationSeconds)
    {
        CycleDurationStep.WithLabels(step).Inc(durationSeconds);
    }

    public void RecordCycleDuration(double durationSeconds)
    {
        CycleDurationTotal.Inc(durationSeconds);
    }

    public void RecordStepSuccess(string step)
    {
        StepSuccessTotal.WithLabels(step).Inc();
    }

    public void RecordStepFailure(string step)
    {
        StepFailureTotal.WithLabels(step).Inc();
    }

    public void RecordRetry(string step)
    {
        RetryTotal.WithLabels(step).Inc();
    }

    public void RecordToolExecution(string toolName, bool success, double durationSeconds)
    {
        var status = success ? "success" : "failure";
        ToolExecutionTotal.WithLabels(toolName, status).Inc();
        ToolExecutionDuration.WithLabels(toolName).Observe(durationSeconds);
    }

    public void UpdateCurrentState(LoopState state)
    {
        CurrentState.Set((double)state);
    }

    public void IncrementCycleCount()
    {
        CycleCount.Inc();
    }
}
