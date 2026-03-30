using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Crypton.Tools;

/// <summary>
/// Extension methods for registering Crypton tool services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class CryptonToolsExtensions
{
    /// <summary>
    /// Registers <see cref="IToolExecutor"/>, <see cref="IHttpClientFactory"/>, and all nine
    /// tool implementations as concrete singletons and as <see cref="IAgentTool"/> registrations.
    /// </summary>
    /// <remarks>
    /// <see cref="IMailboxService"/> is NOT registered here — callers must register it before
    /// calling this method (e.g. <c>services.AddSingleton&lt;IMailboxService&gt;(new FileMailboxService(...))</c>).
    /// </remarks>
    public static IServiceCollection AddCryptonTools(
        this IServiceCollection services,
        ToolsConfig config)
    {
        services.AddHttpClient();
        services.AddSingleton<IToolExecutor>(new ResilientToolExecutor(config));

        var toolCfg = config;

        services.AddSingleton(sp => new Tools.WebSearchTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.WebSearchTool)),
            toolCfg.BraveSearch.ApiKey,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.WebSearchTool>());

        services.AddSingleton(sp => new Tools.WebFetchTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.WebFetchTool)),
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.WebFetchTool>());

        services.AddSingleton(sp => new Tools.BirdTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.BirdTool)),
            toolCfg.Bird.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.BirdTool>());

        services.AddSingleton(sp => new Tools.GetPriceTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.GetPriceTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.GetPriceTool>());

        services.AddSingleton(sp => new Tools.CurrentPositionTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.CurrentPositionTool)),
            toolCfg.ExecutionService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.CurrentPositionTool>());

        services.AddSingleton(sp => new Tools.TechnicalIndicatorsTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.TechnicalIndicatorsTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.TechnicalIndicatorsTool>());

        services.AddSingleton(sp => new Tools.MacroSignalsTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.MacroSignalsTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.MacroSignalsTool>());

        services.AddSingleton(sp => new Tools.OrderBookTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(Tools.OrderBookTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.OrderBookTool>());

        services.AddSingleton(sp => new Tools.SendMailTool(
            sp.GetRequiredService<IMailboxService>()));
        services.AddSingleton<IAgentTool>(sp => sp.GetRequiredService<Tools.SendMailTool>());

        return services;
    }
}
