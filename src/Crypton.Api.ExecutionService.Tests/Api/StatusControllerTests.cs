using Crypton.Api.ExecutionService.Api;
using Crypton.Api.ExecutionService.Execution;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Api;

public sealed class StatusControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IOperationModeService _mode = Substitute.For<IOperationModeService>();
    private readonly ISafeModeController _safeMode = Substitute.For<ISafeModeController>();
    private readonly IStrategyService _strategy = Substitute.For<IStrategyService>();
    private readonly PositionRegistry _registry;
    private readonly InMemoryEventLogger _logger = new();

    public StatusControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _logger,
            NullLogger<PositionRegistry>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private Crypton.Api.ExecutionService.Execution.MarketDataHub MakeMarketDataHub()
    {
        var exchange = Substitute.For<Crypton.Api.ExecutionService.Exchange.IExchangeAdapter>();
        var stratSvc = Substitute.For<Crypton.Api.ExecutionService.Strategy.IStrategyService>();

        // MarketDataHub takes the concrete StrategyService — create a minimal real one
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Microsoft.Extensions.Options.Options.Create(
            new Crypton.Api.ExecutionService.Configuration.ExecutionServiceConfig
            {
                Strategy = new Crypton.Api.ExecutionService.Configuration.StrategyConfig { WatchPath = dir }
            });
        var realSvc = new Crypton.Api.ExecutionService.Strategy.StrategyService(
            cfg,
            new Crypton.Api.ExecutionService.Strategy.StrategyValidator(),
            new Crypton.Api.ExecutionService.Strategy.Conditions.ConditionParser(),
            _logger,
            NullLogger<Crypton.Api.ExecutionService.Strategy.StrategyService>.Instance);
        return new Crypton.Api.ExecutionService.Execution.MarketDataHub(
            exchange, realSvc, NullLogger<Crypton.Api.ExecutionService.Execution.MarketDataHub>.Instance);
    }

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatus_ReturnsOk_WithExpectedFields()
    {
        _mode.CurrentMode.Returns("paper");
        _safeMode.IsActive.Returns(false);
        _strategy.State.Returns(StrategyState.Active);
        _strategy.ActiveStrategyId.Returns("strat-123");

        var controller = new StatusController(_mode, _safeMode, _strategy, _registry, MakeMarketDataHub());
        var result = controller.GetStatus() as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);

        var body = result.Value!;
        var properties = body.GetType().GetProperties();
        var dict = properties.ToDictionary(p => p.Name, p => p.GetValue(body));

        dict["mode"].Should().Be("paper");
        dict["safe_mode"].Should().Be(false);
        dict["strategy_state"].Should().Be("active");
        dict["strategy_id"].Should().Be("strat-123");
    }

    [Fact]
    public void GetStatus_ShowsSafeMode_WhenActive()
    {
        _mode.CurrentMode.Returns("paper");
        _safeMode.IsActive.Returns(true);
        _strategy.State.Returns(StrategyState.None);

        var controller = new StatusController(_mode, _safeMode, _strategy, _registry, MakeMarketDataHub());
        var result = controller.GetStatus() as OkObjectResult;

        result.Should().NotBeNull();
        var dict = result!.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(result.Value));
        dict["safe_mode"].Should().Be(true);
    }

    [Fact]
    public void GetStrategy_ReturnsNotFound_WhenNoStrategy()
    {
        _strategy.ActiveStrategy.Returns((StrategyDocument?)null);
        var controller = new StrategyController(_strategy);

        var result = controller.GetStrategy();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void GetStrategy_ReturnsStrategy_WhenActive()
    {
        var doc = new StrategyDocument
        {
            Id = "test-strat",
            Mode = "paper",
            Posture = "moderate",
            ValidityWindow = DateTimeOffset.UtcNow.AddHours(24),
            PortfolioRisk = new PortfolioRisk
            {
                MaxDrawdownPct = 0.1m,
                MaxTotalExposurePct = 0.8m,
                MaxPerPositionPct = 0.2m,
                DailyLossLimitUsd = 500m
            }
        };
        _strategy.ActiveStrategy.Returns(doc);

        var controller = new StrategyController(_strategy);
        var result = controller.GetStrategy() as OkObjectResult;

        result.Should().NotBeNull();
        (result!.Value as StrategyDocument)!.Id.Should().Be("test-strat");
    }

    [Fact]
    public void GetPositions_ReturnsEmptyList_WhenNoPositions()
    {
        var controller = new PositionsController(_registry);
        var result = controller.GetPositions() as OkObjectResult;

        result.Should().NotBeNull();
        (result!.Value as System.Collections.Generic.IEnumerable<OpenPosition>).Should().BeEmpty();
    }
}
