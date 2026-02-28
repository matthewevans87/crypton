using Crypton.Api.ExecutionService.Api;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Api;

public sealed class OperatorControllerTests
{
    private const string ValidApiKey = "test-api-key";

    private static ApiKeyAuthFilter MakeFilter(string configuredKey = ValidApiKey)
        => new(Options.Create(new ExecutionServiceConfig
        {
            Api = new ApiConfig { ApiKey = configuredKey }
        }));

    private static ActionExecutingContext MakeContext(string method, string? providedKey = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (providedKey is not null)
            httpContext.Request.Headers["X-Api-Key"] = providedKey;

        return new ActionExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            new object());
    }

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApiKeyFilter_Get_AlwaysAllowed()
    {
        var filter = MakeFilter();
        var ctx = MakeContext("GET");

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeNull();
    }

    [Fact]
    public void ApiKeyFilter_Post_ValidKey_Allowed()
    {
        var filter = MakeFilter();
        var ctx = MakeContext("POST", ValidApiKey);

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeNull();
    }

    [Fact]
    public void ApiKeyFilter_Post_MissingKey_Unauthorized()
    {
        var filter = MakeFilter();
        var ctx = MakeContext("POST");

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public void ApiKeyFilter_Post_WrongKey_Unauthorized()
    {
        var filter = MakeFilter();
        var ctx = MakeContext("POST", "wrong-key");

        filter.OnActionExecuting(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // Controller-level tests using NSubstitute

    [Fact]
    public async Task ActivateSafeMode_Returns204_WhenBodyProvided()
    {
        var safeMode = Substitute.For<ISafeModeController>();
        var mode = Substitute.For<IOperationModeService>();
        var strategy = Substitute.For<IStrategyService>();

        var controller = new OperatorController(safeMode, mode, strategy);
        var result = await controller.ActivateSafeMode(new SafeModeActivateRequest("manual test"));

        result.Should().BeOfType<NoContentResult>();
        await safeMode.Received(1).ActivateAsync("manual test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateSafeMode_Returns400_WhenReasonEmpty()
    {
        var safeMode = Substitute.For<ISafeModeController>();
        var mode = Substitute.For<IOperationModeService>();
        var strategy = Substitute.For<IStrategyService>();

        var controller = new OperatorController(safeMode, mode, strategy);
        var result = await controller.ActivateSafeMode(new SafeModeActivateRequest(""));

        result.Should().BeOfType<BadRequestObjectResult>();
        await safeMode.DidNotReceive().ActivateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToLive_Returns204()
    {
        var safeMode = Substitute.For<ISafeModeController>();
        var mode = Substitute.For<IOperationModeService>();
        var strategy = Substitute.For<IStrategyService>();

        var controller = new OperatorController(safeMode, mode, strategy);
        var result = await controller.PromoteToLive(new OperatorNoteRequest("going live"));

        result.Should().BeOfType<NoContentResult>();
        await mode.Received(1).PromoteToLiveAsync("going live", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DemoteToPaper_Returns204()
    {
        var safeMode = Substitute.For<ISafeModeController>();
        var mode = Substitute.For<IOperationModeService>();
        var strategy = Substitute.For<IStrategyService>();

        var controller = new OperatorController(safeMode, mode, strategy);
        var result = await controller.DemoteToPaper(new OperatorNoteRequest(null));

        result.Should().BeOfType<NoContentResult>();
        await mode.Received(1).DemoteToPaperAsync(string.Empty, Arg.Any<CancellationToken>());
    }
}
