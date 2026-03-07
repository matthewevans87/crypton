using AgentRunner.Agents;
using AgentRunner.Configuration;
using AgentRunner.Startup;
using Xunit;

namespace AgentRunner.Tests.Startup;

public class AgentRunnerStartupCoordinatorTests
{
    [Fact]
    public async Task TryStartAsync_ValidationFails_EntersDegradedAndDoesNotStartLoop()
    {
        var validator = new FakeStartupValidator(StartupValidationResultForErrors("Execution Service unavailable"));
        var availability = new ServiceAvailabilityState();
        var runner = new FakeAgentRunnerLifecycle();
        var coordinator = new AgentRunnerStartupCoordinator(validator, availability, runner, CreateConfig());

        var result = await coordinator.TryStartAsync();

        Assert.False(result.Started);
        Assert.True(result.IsDegraded);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(0, runner.StartCallCount);
        Assert.True(availability.IsDegraded);
        Assert.Single(availability.Errors);
    }

    [Fact]
    public async Task TryStartAsync_AfterRecovery_ClearsDegradedAndStartsLoop()
    {
        var validator = new FakeStartupValidator(
            StartupValidationResultForErrors("Market Data unavailable"),
            StartupValidationResultForSuccess());
        var availability = new ServiceAvailabilityState();
        var runner = new FakeAgentRunnerLifecycle();
        var coordinator = new AgentRunnerStartupCoordinator(validator, availability, runner, CreateConfig());

        var degradedResult = await coordinator.TryStartAsync();
        var recoveredResult = await coordinator.TryStartAsync();

        Assert.False(degradedResult.Started);
        Assert.True(degradedResult.IsDegraded);

        Assert.True(recoveredResult.Started);
        Assert.False(recoveredResult.IsDegraded);
        Assert.Equal(2, validator.CallCount);
        Assert.Equal(1, runner.StartCallCount);
        Assert.False(availability.IsDegraded);
        Assert.Empty(availability.Errors);
    }

    [Fact]
    public async Task TryStartAsync_AlreadyRunning_DoesNotRevalidateOrRestart()
    {
        var validator = new FakeStartupValidator(StartupValidationResultForSuccess());
        var availability = new ServiceAvailabilityState();
        var runner = new FakeAgentRunnerLifecycle { IsRunning = true };
        var coordinator = new AgentRunnerStartupCoordinator(validator, availability, runner, CreateConfig());

        var result = await coordinator.TryStartAsync();

        Assert.False(result.Started);
        Assert.False(result.IsDegraded);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(0, runner.StartCallCount);
    }

    private static AgentRunnerConfig CreateConfig() => new()
    {
        Ollama = new OllamaConfig { BaseUrl = "http://localhost:11434" },
        Tools = new ToolConfig
        {
            BraveSearch = new BraveSearchConfig { ApiKey = "test-key" },
            ExecutionService = new ExecutionServiceConfig { BaseUrl = "http://localhost:5000" },
            MarketDataService = new MarketDataServiceConfig { BaseUrl = "http://localhost:5002" }
        }
    };

    private static StartupValidationResult StartupValidationResultForSuccess() => new(true, []);

    private static StartupValidationResult StartupValidationResultForErrors(params string[] errors) =>
        new(false, errors);

    private sealed class FakeStartupValidator : IStartupValidator
    {
        private readonly Queue<StartupValidationResult> _results;

        public FakeStartupValidator(params StartupValidationResult[] results)
        {
            _results = new Queue<StartupValidationResult>(results);
        }

        public int CallCount { get; private set; }

        public Task<StartupValidationResult> ValidateAsync(AgentRunnerConfig config, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_results.Count == 0)
            {
                return Task.FromResult(new StartupValidationResult(true, []));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeAgentRunnerLifecycle : IAgentRunnerLifecycle
    {
        public bool IsRunning { get; set; }
        public int StartCallCount { get; private set; }

        public Task StartAsync()
        {
            StartCallCount++;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
    }
}
