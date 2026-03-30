using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Crypton.Tools;

/// <summary>
/// Extension methods for registering Crypton tool services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class CryptonToolsExtensions
{
    /// <summary>
    /// Registers <see cref="IToolRegistry"/>, <see cref="IVirtualTerminal"/>,
    /// <see cref="IHttpClientFactory"/>, and all tool commands as singleton instances.
    /// </summary>
    /// <remarks>
    /// <see cref="IMailboxService"/> is NOT registered here — callers must register it before
    /// calling this method.
    /// </remarks>
    public static IServiceCollection AddCryptonTools(
        this IServiceCollection services,
        ToolsConfig config)
    {
        services.AddHttpClient();

        var registry = new ToolRegistry();

        // We resolve HttpClients lazily from a factory — stored so each registration
        // can close over its own client name.
        services.AddSingleton<IToolRegistry>(sp =>
        {
            RegisterAllTools(registry, sp, config);
            return registry;
        });

        services.AddSingleton<IVirtualTerminal>(sp =>
            new InProcessVirtualTerminal(sp.GetRequiredService<IToolRegistry>(), config));

        return services;
    }

    private static void RegisterAllTools(ToolRegistry registry, IServiceProvider sp, ToolsConfig config)
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        // web_search
        registry.Register<WebSearchTool, WebSearchTool.Settings>(
            "web_search",
            console =>
            {
                var tool = new WebSearchTool(factory.CreateClient(nameof(WebSearchTool)), config.BraveSearch.ApiKey, console);
                var registrar = BuildRegistrar(typeof(WebSearchTool), tool);
                var app = new CommandApp<WebSearchTool>(registrar);
                app.Configure(c => c.SetApplicationName("web_search").PropagateExceptions());
                return app;
            });

        // web_fetch
        registry.Register<WebFetchTool, WebFetchTool.Settings>(
            "web_fetch",
            console =>
            {
                var tool = new WebFetchTool(factory.CreateClient(nameof(WebFetchTool)), console);
                var registrar = BuildRegistrar(typeof(WebFetchTool), tool);
                var app = new CommandApp<WebFetchTool>(registrar);
                app.Configure(c => c.SetApplicationName("web_fetch").PropagateExceptions());
                return app;
            });

        // bird
        registry.Register<BirdTool, BirdTool.Settings>(
            "bird",
            console =>
            {
                var tool = new BirdTool(factory.CreateClient(nameof(BirdTool)), config.Bird.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(BirdTool), tool);
                var app = new CommandApp<BirdTool>(registrar);
                app.Configure(c => c.SetApplicationName("bird").PropagateExceptions());
                return app;
            });

        // get_price
        registry.Register<GetPriceTool, GetPriceTool.Settings>(
            "get_price",
            console =>
            {
                var tool = new GetPriceTool(factory.CreateClient(nameof(GetPriceTool)), config.MarketDataService.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(GetPriceTool), tool);
                var app = new CommandApp<GetPriceTool>(registrar);
                app.Configure(c => c.SetApplicationName("get_price").PropagateExceptions());
                return app;
            });

        // current_position
        registry.Register<CurrentPositionTool, CurrentPositionTool.Settings>(
            "current_position",
            console =>
            {
                var tool = new CurrentPositionTool(factory.CreateClient(nameof(CurrentPositionTool)), config.ExecutionService.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(CurrentPositionTool), tool);
                var app = new CommandApp<CurrentPositionTool>(registrar);
                app.Configure(c => c.SetApplicationName("current_position").PropagateExceptions());
                return app;
            });

        // technical_indicators
        registry.Register<TechnicalIndicatorsTool, TechnicalIndicatorsTool.Settings>(
            "technical_indicators",
            console =>
            {
                var tool = new TechnicalIndicatorsTool(factory.CreateClient(nameof(TechnicalIndicatorsTool)), config.MarketDataService.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(TechnicalIndicatorsTool), tool);
                var app = new CommandApp<TechnicalIndicatorsTool>(registrar);
                app.Configure(c => c.SetApplicationName("technical_indicators").PropagateExceptions());
                return app;
            });

        // macro_signals
        registry.Register<MacroSignalsTool, MacroSignalsTool.Settings>(
            "macro_signals",
            console =>
            {
                var tool = new MacroSignalsTool(factory.CreateClient(nameof(MacroSignalsTool)), config.MarketDataService.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(MacroSignalsTool), tool);
                var app = new CommandApp<MacroSignalsTool>(registrar);
                app.Configure(c => c.SetApplicationName("macro_signals").PropagateExceptions());
                return app;
            });

        // order_book
        registry.Register<OrderBookTool, OrderBookTool.Settings>(
            "order_book",
            console =>
            {
                var tool = new OrderBookTool(factory.CreateClient(nameof(OrderBookTool)), config.MarketDataService.BaseUrl, console);
                var registrar = BuildRegistrar(typeof(OrderBookTool), tool);
                var app = new CommandApp<OrderBookTool>(registrar);
                app.Configure(c => c.SetApplicationName("order_book").PropagateExceptions());
                return app;
            });

        // send_mail — needs IMailboxService from the outer IServiceProvider
        registry.Register<SendMailTool, SendMailTool.Settings>(
            "send_mail",
            console =>
            {
                var mailbox = sp.GetRequiredService<IMailboxService>();
                var tool = new SendMailTool(mailbox, console);
                var registrar = BuildRegistrar(typeof(SendMailTool), tool);
                var app = new CommandApp<SendMailTool>(registrar);
                app.Configure(c => c.SetApplicationName("send_mail").PropagateExceptions());
                return app;
            });
    }

    private static InProcessTypeRegistrar BuildRegistrar(Type commandType, object instance)
    {
        var registrar = new InProcessTypeRegistrar();
        registrar.RegisterInstance(commandType, instance);
        return registrar;
    }
}
