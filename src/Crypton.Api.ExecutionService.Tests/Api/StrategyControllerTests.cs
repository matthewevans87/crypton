using System.Text;
using Crypton.Api.ExecutionService.Api;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Api;

/// <summary>
/// Tests for the <c>POST /strategy/push</c> REST endpoint introduced in 2a.3.
/// </summary>
public sealed class StrategyControllerTests
{
    private readonly IStrategyService _strategy = Substitute.For<IStrategyService>();

    private StrategyController CreateController(bool enableRestEndpoint = true, string body = "")
    {
        var config = Options.Create(new ExecutionServiceConfig
        {
            Strategy = new StrategyConfig { EnableRestEndpoint = enableRestEndpoint }
        });

        var controller = new StrategyController(_strategy, config);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    // ── POST /strategy/push ───────────────────────────────────────────────────

    [Fact]
    public async Task PushStrategy_WhenRestEndpointDisabled_Returns409()
    {
        var controller = CreateController(enableRestEndpoint: false, body: "{}");

        var result = await controller.PushStrategy(CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task PushStrategy_WhenBodyEmpty_Returns400()
    {
        var controller = CreateController(body: "");

        var result = await controller.PushStrategy(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PushStrategy_WhenBodyIsWhitespace_Returns400()
    {
        var controller = CreateController(body: "   \t\n");

        var result = await controller.PushStrategy(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PushStrategy_WhenLoadFromJsonReturnsError_Returns422()
    {
        _strategy.LoadFromJsonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("validity_window: validity_window is in the past; strategy is already expired on load.");

        var controller = CreateController(body: "{\"some\":\"json\"}");

        var result = await controller.PushStrategy(CancellationToken.None);

        var unprocessable = result.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        unprocessable.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task PushStrategy_WhenLoadSucceeds_Returns200WithStrategyId()
    {
        _strategy.LoadFromJsonAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _strategy.ActiveStrategyId.Returns("abc123def456abcd");

        var controller = CreateController(body: "{\"some\":\"json\"}");

        var result = await controller.PushStrategy(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);

        var strategyId = ok.Value!.GetType().GetProperty("StrategyId")!.GetValue(ok.Value);
        strategyId.Should().Be("abc123def456abcd");
    }

    [Fact]
    public async Task PushStrategy_PassesBodyVerbatimToLoadFromJsonAsync()
    {
        var capturedJson = string.Empty;
        _strategy.LoadFromJsonAsync(Arg.Do<string>(j => capturedJson = j), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _strategy.ActiveStrategyId.Returns("id1");

        const string payload = "{\"mode\":\"paper\",\"posture\":\"moderate\"}";
        var controller = CreateController(body: payload);

        await controller.PushStrategy(CancellationToken.None);

        capturedJson.Should().Be(payload);
    }

    // ── GET /strategy ─────────────────────────────────────────────────────────

    [Fact]
    public void GetStrategy_WhenNoActiveStrategy_ReturnsNotFound()
    {
        _strategy.ActiveStrategy.Returns((StrategyDocument?)null);
        var config = Options.Create(new ExecutionServiceConfig());
        var controller = new StrategyController(_strategy, config);

        var result = controller.GetStrategy();

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
