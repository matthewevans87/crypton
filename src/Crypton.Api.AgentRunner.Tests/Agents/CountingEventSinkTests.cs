using AgentRunner.Abstractions;
using AgentRunner.Domain.Events;
using AgentRunner.Execution;
using NSubstitute;
using Xunit;

namespace AgentRunner.Tests.Agents;

/// <summary>
/// Unit tests for <see cref="LlmAgentExecutor.CountingEventSink"/>, which bridges
/// tool call events to <see cref="IEventLogger"/> for file-based logging.
/// </summary>
public class CountingEventSinkTests
{
    private static (LlmAgentExecutor.CountingEventSink Sink, IAgentEventSink Inner, IEventLogger Logger)
        MakeSink(string agentName = "Plan", string cycleId = "cycle-001")
    {
        var inner = Substitute.For<IAgentEventSink>();
        var logger = Substitute.For<IEventLogger>();
        var sink = new LlmAgentExecutor.CountingEventSink(inner, logger, agentName, cycleId);
        return (sink, inner, logger);
    }

    [Fact]
    public void Publish_ToolCallStartedEvent_IncrementsCount()
    {
        var (sink, _, _) = MakeSink();
        var evt = new ToolCallStartedEvent(Guid.NewGuid(), "web_search", "{}", "Plan");

        sink.Publish(evt);

        Assert.Equal(1, sink.ToolCallCount);
    }

    [Fact]
    public void Publish_ToolCallStartedEvent_ForwardsToInnerSink()
    {
        var (sink, inner, _) = MakeSink();
        var evt = new ToolCallStartedEvent(Guid.NewGuid(), "web_search", "{}", "Plan");

        sink.Publish(evt);

        inner.Received(1).Publish(evt);
    }

    [Fact]
    public void Publish_ToolCallCompletedEvent_CallsLogToolExecution()
    {
        var (sink, _, logger) = MakeSink(agentName: "Research", cycleId: "cycle-999");
        var id = Guid.NewGuid();
        var started = new ToolCallStartedEvent(id, "web_fetch", "{\"url\":\"http://x.com\"}", "Research");
        var completed = new ToolCallCompletedEvent(id, "web_fetch", "<html>...</html>", TimeSpan.FromMilliseconds(500), IsError: false, StepName: "Research");

        sink.Publish(started);
        sink.Publish(completed);

        logger.Received(1).LogToolExecution("web_fetch", "{\"url\":\"http://x.com\"}", 500);
    }

    [Fact]
    public void Publish_ToolCallCompletedEvent_CallsLogToolCallJournal()
    {
        var (sink, _, logger) = MakeSink(agentName: "Plan", cycleId: "cycle-042");
        var id = Guid.NewGuid();
        var started = new ToolCallStartedEvent(id, "get_price", "{\"pair\":\"BTC/USD\"}", "Plan");
        var completed = new ToolCallCompletedEvent(id, "get_price", "65000", TimeSpan.FromMilliseconds(123), IsError: false, StepName: "Plan");

        sink.Publish(started);
        sink.Publish(completed);

        logger.Received(1).LogToolCallJournal("Plan", "cycle-042", 1, "get_price", "{\"pair\":\"BTC/USD\"}", "65000", 123);
    }

    [Fact]
    public void Publish_ToolCallCompletedEvent_ForwardsToInnerSink()
    {
        var (sink, inner, _) = MakeSink();
        var id = Guid.NewGuid();
        var started = new ToolCallStartedEvent(id, "web_search", "{}", "Plan");
        var completed = new ToolCallCompletedEvent(id, "web_search", "results", TimeSpan.FromMilliseconds(200), IsError: false, StepName: "Plan");

        sink.Publish(started);
        sink.Publish(completed);

        inner.Received(1).Publish(started);
        inner.Received(1).Publish(completed);
    }

    [Fact]
    public void Publish_MultipleToolCalls_CountsStartedEvents()
    {
        var (sink, _, logger) = MakeSink();

        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            sink.Publish(new ToolCallStartedEvent(id, "web_search", "{}", "Plan"));
            sink.Publish(new ToolCallCompletedEvent(id, "web_search", "ok", TimeSpan.FromMilliseconds(100), IsError: false, StepName: "Plan"));
        }

        Assert.Equal(3, sink.ToolCallCount);
        logger.Received(3).LogToolExecution(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>());
    }

    [Fact]
    public void Publish_CompletedWithoutMatchingStarted_UsesEmptyInputJson()
    {
        var (sink, _, logger) = MakeSink(agentName: "Analyze", cycleId: "cycle-007");
        var completed = new ToolCallCompletedEvent(Guid.NewGuid(), "bird", "tweet", TimeSpan.FromMilliseconds(50), IsError: false, StepName: "Analyze");

        sink.Publish(completed);

        logger.Received(1).LogToolExecution("bird", "{}", 50);
        logger.Received(1).LogToolCallJournal("Analyze", "cycle-007", 0, "bird", "{}", "tweet", 50);
    }

    [Fact]
    public void Publish_NonToolCallEvent_ForwardsWithoutLogging()
    {
        var (sink, inner, logger) = MakeSink();
        var evt = new TokenReceivedEvent("hello", "Plan");

        sink.Publish(evt);

        inner.Received(1).Publish(evt);
        logger.DidNotReceiveWithAnyArgs().LogToolExecution(default!, default!, default);
    }
}
