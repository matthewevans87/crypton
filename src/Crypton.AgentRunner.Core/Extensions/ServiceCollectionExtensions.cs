using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using AgentRunner.Execution;
using AgentRunner.Execution.Tools;
using AgentRunner.Infrastructure;
using AgentRunner.Logging;
using AgentRunner.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace AgentRunner.Extensions;

/// <summary>
/// Extension methods for registering Agent Runner services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core orchestration and infrastructure services.
    /// Does NOT register <see cref="IAgentExecutor"/> — call
    /// <see cref="AddLlmExecution"/> or provide your own implementation.
    /// </summary>
    public static IServiceCollection AddAgentRunnerCore(
        this IServiceCollection services,
        AgentRunnerConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(BuildStateDefinitions());

        services.AddSingleton<IEventLogger>(sp => new EventLogger(
            logPath: Path.Combine(config.Logging.OutputPath, "agent_runner.log"),
            cyclesBasePath: Path.Combine(config.Storage.BasePath, config.Storage.CyclesPath),
            maxFileSizeMb: config.Logging.MaxFileSizeMb,
            maxFileCount: config.Logging.MaxFileCount,
            capturePrompts: config.Logging.CapturePrompts));
        services.AddSingleton<ILoopStateMachine, LoopStateMachine>();
        services.AddSingleton<IArtifactStore>(sp => new FileSystemArtifactStore(config.Storage));
        services.AddSingleton<IMailboxService>(sp => new FileMailboxService(config.Storage));
        services.AddSingleton<IStatePersistence>(sp =>
            new JsonStatePersistence(Path.Combine(config.Storage.BasePath, "state.json")));
        services.AddSingleton<ICycleScheduler, CycleScheduler>();
        services.AddSingleton<IAgentContextProvider>(sp => new AgentContextProvider(
            sp.GetRequiredService<IArtifactStore>(),
            sp.GetRequiredService<IMailboxService>(),
            sp.GetRequiredService<IReadOnlyDictionary<LoopState, AgentStateDefinition>>(),
            config));
        services.AddSingleton<LoopRestartManager>();
        services.AddSingleton<ICycleOrchestrator, CycleOrchestrator>();

        return services;
    }

    /// <summary>Registers all tool implementations and the <see cref="IToolProvider"/>.</summary>
    public static IServiceCollection AddAgentTools(
        this IServiceCollection services,
        AgentRunnerConfig config)
    {
        services.AddHttpClient();
        services.AddSingleton<IToolExecutor>(sp => new ResilientToolExecutor(config));

        var toolCfg = config.Tools;

        services.AddSingleton<IAgentTool>(sp => new WebSearchTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WebSearchTool)),
            toolCfg.BraveSearch.ApiKey,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new WebFetchTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WebFetchTool)),
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new BirdTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BirdTool)),
            toolCfg.Bird.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new GetPriceTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GetPriceTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new CurrentPositionTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CurrentPositionTool)),
            toolCfg.ExecutionService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new TechnicalIndicatorsTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TechnicalIndicatorsTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new MacroSignalsTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MacroSignalsTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new OrderBookTool(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OrderBookTool)),
            toolCfg.MarketDataService.BaseUrl,
            sp.GetRequiredService<IToolExecutor>()));

        services.AddSingleton<IAgentTool>(sp => new SendMailTool(
            sp.GetRequiredService<IMailboxService>()));

        services.AddSingleton<IToolProvider, ToolRegistry>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="OllamaApiClient"/> as <see cref="IChatClient"/> and
    /// wires up <see cref="LlmAgentExecutor"/> as <see cref="IAgentExecutor"/>.
    /// </summary>
    public static IServiceCollection AddLlmExecution(
        this IServiceCollection services,
        AgentRunnerConfig config)
    {
        services.AddSingleton<IChatClient>(new OllamaApiClient(new Uri(config.Ollama.BaseUrl)));
        services.AddSingleton<IAgentExecutor, LlmAgentExecutor>();
        return services;
    }

    // ─── State definition table ────────────────────────────────────────────────

    private static IReadOnlyDictionary<LoopState, AgentStateDefinition> BuildStateDefinitions() =>
        new Dictionary<LoopState, AgentStateDefinition>
        {
            [LoopState.Plan] = new(
                AgentName: "Plan",
                PromptFile: "plan_agent.md",
                TemplateFile: "plan.md",
                InputArtifacts: [],
                AvailableTools: ["web_search", "web_fetch", "bird", "technical_indicators", "send_mail"],
                IncludeRecentEvaluations: true,
                RecentEvaluationCount: 7),

            [LoopState.Research] = new(
                AgentName: "Research",
                PromptFile: "research_agent.md",
                TemplateFile: "research.md",
                InputArtifacts: ["plan.md"],
                AvailableTools: ["web_search", "web_fetch", "bird", "technical_indicators", "send_mail"]),

            [LoopState.Analyze] = new(
                AgentName: "Analysis",
                PromptFile: "analysis_agent.md",
                TemplateFile: "analysis.md",
                InputArtifacts: ["research.md"],
                AvailableTools: ["current_position", "technical_indicators", "macro_signals", "order_book", "send_mail"]),

            [LoopState.Synthesize] = new(
                AgentName: "Synthesis",
                PromptFile: "synthesis_agent.md",
                TemplateFile: "strategy.json",
                InputArtifacts: ["analysis.md"],
                AvailableTools: ["current_position", "send_mail"]),

            [LoopState.Evaluate] = new(
                AgentName: "Evaluation",
                PromptFile: "evaluation_agent.md",
                TemplateFile: "evaluation.md",
                InputArtifacts: ["strategy.json", "analysis.md"],
                AvailableTools: ["current_position", "send_mail"],
                IncludeMemory: false,
                IncludeRecentEvaluations: true,
                RecentEvaluationCount: 3),
        };
}
