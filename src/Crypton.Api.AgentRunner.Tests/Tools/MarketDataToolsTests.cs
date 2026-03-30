using System.Net;
using System.Text;
using System.Text.Json;
using Crypton.Tools;
using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Spectre.Console.Cli;
using Xunit;

namespace AgentRunner.Tests.Tools;

/// <summary>
/// Integration tests for the market-data and position tool commands.
/// Each test wires a tool into <see cref="InProcessVirtualTerminal"/> and verifies
/// the output written to the capturing <see cref="Spectre.Console.IAnsiConsole"/>.
/// </summary>
public class MarketDataToolsTests
{
    private static readonly ToolsConfig Config = new() { MaxRetries = 0, DefaultTimeoutSeconds = 30 };

    private static HttpClient FakeClient(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpClient(new FakeHandler(status, json));
    }

    private static (ToolRegistry Registry, InProcessVirtualTerminal Vt) BuildVt(
        Action<ToolRegistry> register)
    {
        var registry = new ToolRegistry();
        register(registry);
        var vt = new InProcessVirtualTerminal(registry, Config);
        return (registry, vt);
    }

    // ─── GetPriceTool ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPriceTool_ValidSymbol_ReturnsSuccessWithOutput()
    {
        var response = new { symbol = "BTC/USD", price = 65000.0m, timestamp = "2025-01-01T00:00:00Z" };
        var (_, vt) = BuildVt(r => r.Register<GetPriceTool, GetPriceTool.Settings>(
            "get_price",
            console =>
            {
                var tool = new GetPriceTool(FakeClient(response), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(GetPriceTool), tool);
                var app = new CommandApp<GetPriceTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("get_price", "{\"symbols\": \"BTC/USD\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task GetPriceTool_ServerError_ReturnsFailure()
    {
        var (_, vt) = BuildVt(r => r.Register<GetPriceTool, GetPriceTool.Settings>(
            "get_price",
            console =>
            {
                var tool = new GetPriceTool(FakeClient(new { }, HttpStatusCode.ServiceUnavailable), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(GetPriceTool), tool);
                var app = new CommandApp<GetPriceTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("get_price", "{\"symbols\": \"BTC/USD\"}");

        Assert.False(result.IsSuccess);
    }

    // ─── CurrentPositionTool ──────────────────────────────────────────────────

    [Fact]
    public async Task CurrentPositionTool_ReturnsSuccessWithOutput()
    {
        var response = new { mode = "paper", balance = new { availableUsd = 10000.0m }, openPositions = Array.Empty<object>() };
        var (_, vt) = BuildVt(r => r.Register<CurrentPositionTool, CurrentPositionTool.Settings>(
            "current_position",
            console =>
            {
                var tool = new CurrentPositionTool(FakeClient(response), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(CurrentPositionTool), tool);
                var app = new CommandApp<CurrentPositionTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("current_position", "{}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.NotEmpty(result.Output);
    }

    // ─── MacroSignalsTool ─────────────────────────────────────────────────────

    [Fact]
    public async Task MacroSignalsTool_ReturnsSuccessWithOutput()
    {
        var response = new { trend = "bullish", fearGreedIndex = 65 };
        var (_, vt) = BuildVt(r => r.Register<MacroSignalsTool, MacroSignalsTool.Settings>(
            "macro_signals",
            console =>
            {
                var tool = new MacroSignalsTool(FakeClient(response), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(MacroSignalsTool), tool);
                var app = new CommandApp<MacroSignalsTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("macro_signals", "{}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.NotEmpty(result.Output);
    }

    // ─── OrderBookTool ────────────────────────────────────────────────────────

    [Fact]
    public async Task OrderBookTool_ValidAsset_ReturnsSuccess()
    {
        var response = new { symbol = "BTC/USD", bids = Array.Empty<object>(), asks = Array.Empty<object>() };
        var (_, vt) = BuildVt(r => r.Register<OrderBookTool, OrderBookTool.Settings>(
            "order_book",
            console =>
            {
                var tool = new OrderBookTool(FakeClient(response), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(OrderBookTool), tool);
                var app = new CommandApp<OrderBookTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("order_book", "{\"asset\": \"BTC\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
    }

    // ─── TechnicalIndicatorsTool ──────────────────────────────────────────────

    [Fact]
    public async Task TechnicalIndicatorsTool_ValidParams_ReturnsSuccess()
    {
        var response = new { symbol = "BTC/USD", timeframe = "1d", rsi = 55.0m };
        var (_, vt) = BuildVt(r => r.Register<TechnicalIndicatorsTool, TechnicalIndicatorsTool.Settings>(
            "technical_indicators",
            console =>
            {
                var tool = new TechnicalIndicatorsTool(FakeClient(response), "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(TechnicalIndicatorsTool), tool);
                var app = new CommandApp<TechnicalIndicatorsTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("technical_indicators", "{\"asset\": \"BTC\", \"timeframe\": \"4h\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
    }

    // ─── WebFetchTool ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WebFetchTool_ValidUrl_ReturnsSuccess()
    {
        var html = "<html><body><p>Hello world</p></body></html>";
        var handler = new FakeHandler(HttpStatusCode.OK, html);
        var (_, vt) = BuildVt(r => r.Register<WebFetchTool, WebFetchTool.Settings>(
            "web_fetch",
            console =>
            {
                var tool = new WebFetchTool(new HttpClient(handler), console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(WebFetchTool), tool);
                var app = new CommandApp<WebFetchTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            }));

        var result = await vt.RunAsync("web_fetch", "{\"url\": \"https://example.com\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.Contains("Hello world", result.Output);
    }

    // ─── ToolRegistry ─────────────────────────────────────────────────────────

    [Fact]
    public void TryGetRegistration_KnownTool_ReturnsTrue()
    {
        var (registry, _) = BuildVt(r => r.Register<GetPriceTool, GetPriceTool.Settings>(
            "get_price", _ => new CommandApp<GetPriceTool>()));

        Assert.True(registry.TryGetRegistration("get_price", out var reg));
        Assert.Equal("get_price", reg!.Name);
    }

    [Fact]
    public void TryGetRegistration_UnknownTool_ReturnsFalse()
    {
        var (registry, _) = BuildVt(_ => { });
        Assert.False(registry.TryGetRegistration("nonexistent", out _));
    }

    [Fact]
    public void GetAllDescriptions_ReturnsRegisteredTools()
    {
        var (registry, _) = BuildVt(r =>
        {
            r.Register<GetPriceTool, GetPriceTool.Settings>("get_price", _ => new CommandApp<GetPriceTool>());
            r.Register<MacroSignalsTool, MacroSignalsTool.Settings>("macro_signals", _ => new CommandApp<MacroSignalsTool>());
        });

        var descs = registry.GetAllDescriptions();

        Assert.Equal(2, descs.Count);
        Assert.Contains(descs, d => d.Name == "get_price");
        Assert.Contains(descs, d => d.Name == "macro_signals");
    }

    [Fact]
    public void RunAsync_UnknownTool_ReturnsNonZeroExitCode()
    {
        var (_, vt) = BuildVt(_ => { });
        var result = vt.RunAsync("does_not_exist", "{}").GetAwaiter().GetResult();
        Assert.False(result.IsSuccess);
    }
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    public FakeHandler(HttpStatusCode status, string content)
    {
        _status = status;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        });
}
