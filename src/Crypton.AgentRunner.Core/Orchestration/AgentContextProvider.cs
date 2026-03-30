using System.Text;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using Crypton.Tools;

namespace AgentRunner.Orchestration;

/// <summary>
/// Builds a fully-formed <see cref="AgentInput"/> for the given loop state and cycle.
/// Loads prompt files and output templates from disk (relative to <c>AppContext.BaseDirectory</c>).
/// </summary>
public sealed class AgentContextProvider : IAgentContextProvider
{
    private readonly IArtifactStore _artifacts;
    private readonly IMailboxService _mailbox;
    private readonly IReadOnlyDictionary<LoopState, AgentStateDefinition> _definitions;
    private readonly AgentRunnerConfig _config;
    private readonly IToolRegistry _toolRegistry;

    public AgentContextProvider(
        IArtifactStore artifacts,
        IMailboxService mailbox,
        IReadOnlyDictionary<LoopState, AgentStateDefinition> definitions,
        IToolRegistry toolRegistry,
        AgentRunnerConfig config)
    {
        _artifacts = artifacts;
        _mailbox = mailbox;
        _definitions = definitions;
        _config = config;
        _toolRegistry = toolRegistry;
    }

    public AgentInput BuildContext(LoopState state, string cycleId, string? previousCycleId = null)
    {
        if (!_definitions.TryGetValue(state, out var def))
            throw new InvalidOperationException($"No agent state definition for {state}.");

        var sourceCycleId = state == LoopState.Evaluate
            ? previousCycleId ?? cycleId
            : cycleId;

        var identity = LoadPromptFile(def.PromptFile);
        var toolList = BuildToolList(def.AvailableTools);
        var outputTemplate = LoadTemplate(def.TemplateFile);

        var messages = _mailbox.GetMessages(
            def.AgentName.ToLowerInvariant(), _config.Storage.MaxMailboxMessages);

        var memory = def.IncludeMemory
            ? _artifacts.ReadMemory(def.AgentName.ToLowerInvariant())
            : null;
        var sharedMemory = _artifacts.ReadSharedMemory();

        var recentEvals = def.IncludeRecentEvaluations
            ? _artifacts.GetRecentEvaluations(def.RecentEvaluationCount)
            : (IReadOnlyList<string>)[];

        var inputArtifacts = def.InputArtifacts
            .ToDictionary(name => name, name => _artifacts.Read(sourceCycleId, name) ?? "");

        if (state == LoopState.Evaluate)
            inputArtifacts["evaluated_cycle_id"] = sourceCycleId;

        var systemPrompt = BuildSystemPrompt(identity, toolList);
        var userPrompt = BuildUserPrompt(
            def.AgentName, messages, memory, sharedMemory,
            recentEvals, inputArtifacts, outputTemplate);

        return new AgentInput(
            AgentName: def.AgentName,
            CycleId: cycleId,
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            AvailableTools: def.AvailableTools);
    }

    // ─── Prompt builders ──────────────────────────────────────────────────────

    private static string BuildSystemPrompt(string identity, string toolList)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Identity");
        sb.AppendLine(identity);
        sb.AppendLine();
        sb.AppendLine("# Current Date and Time");
        sb.AppendLine(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));
        sb.AppendLine();
        sb.AppendLine("# Available Tools");
        sb.AppendLine();
        sb.AppendLine("When you need to call a tool, respond ONLY with valid JSON:");
        sb.AppendLine("  {\"tool\": \"<tool_name>\"}");
        sb.AppendLine();
        sb.AppendLine("You will then be prompted for the tool's arguments.");
        sb.AppendLine("When you have no more tool calls to make, respond with:");
        sb.AppendLine("  {\"tool\": null}");
        sb.AppendLine();
        sb.AppendLine("Tools available to you:");
        sb.AppendLine(toolList);
        return sb.ToString();
    }

    private static string BuildUserPrompt(
        string agentName,
        IReadOnlyList<MailboxMessage> messages,
        string? memory,
        string? sharedMemory,
        IReadOnlyList<string> recentEvals,
        Dictionary<string, string> inputArtifacts,
        string outputTemplate)
    {
        var sb = new StringBuilder();

        if (messages.Count > 0)
        {
            sb.AppendLine("# Mailbox Messages");
            foreach (var msg in messages)
                sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm}] From {msg.FromAgent}: {msg.Content}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(memory))
        {
            sb.AppendLine("# Memory");
            sb.AppendLine(memory);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(sharedMemory))
        {
            sb.AppendLine("# Shared Memory (Cross-Cycle Context)");
            sb.AppendLine(sharedMemory);
            sb.AppendLine();
        }

        if (recentEvals.Count > 0)
        {
            sb.AppendLine("# Recent Evaluations");
            for (var i = 0; i < recentEvals.Count; i++)
            {
                sb.AppendLine($"## Evaluation {i + 1}");
                sb.AppendLine(recentEvals[i]);
            }
            sb.AppendLine();
        }

        if (inputArtifacts.Count > 0)
        {
            sb.AppendLine("# Input Artifacts");
            foreach (var (name, content) in inputArtifacts.Where(kv => kv.Key != "evaluated_cycle_id"))
            {
                sb.AppendLine($"## {name}");
                sb.AppendLine(content);
            }
            sb.AppendLine();
        }

        sb.AppendLine("# Output Template");
        sb.AppendLine(outputTemplate);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("**BEGIN your work now.**");
        sb.AppendLine();
        sb.AppendLine("Important reminders:");
        sb.AppendLine("- Tool calls are executed by the host system and return REAL data. Do NOT invent results or write pseudocode.");
        sb.AppendLine("- You MUST call tools BEFORE writing your output document. Do not assume or invent data.");
        sb.AppendLine("- Call each tool, wait for its result, then proceed to the next step.");
        sb.AppendLine("- Follow the procedure in your identity instructions step by step.");
        sb.AppendLine("- **IMPORTANT — No preamble or postamble.** When writing your output document, start the document IMMEDIATELY on the first line. Do NOT include sentences like 'Based on the data gathered...' or 'Here is the draft of...' before the document. Your entire response should be the document itself.");
        sb.AppendLine("- **Replace all template placeholders.** The Output Template contains example placeholder text like `[Signal name]`, `[Headline / Event]`, `<!-- date -->`, etc. Replace every placeholder with real content from your research. If there is nothing to report for a section, write 'None identified this cycle.' Do NOT copy placeholder brackets literally into your output.");
        sb.AppendLine("- **STOP at the end of your document.** The '---' separator above and the text 'BEGIN your work now.' are system triggers, NOT part of your output document. Do NOT copy the phrase 'BEGIN your work now.' anywhere in your output. Your document must end at its natural conclusion.");

        AppendAgentSpecificGuidance(sb, agentName);

        return sb.ToString();
    }

    private static void AppendAgentSpecificGuidance(StringBuilder sb, string agentName)
    {
        if (agentName == "Synthesis")
        {
            sb.AppendLine();
            sb.AppendLine("**SYNTHESIS AGENT SPECIFIC RULE:**");
            sb.AppendLine("Your ENTIRE response must be ONLY raw, valid JSON. This is critical because your output is saved directly as `strategy.json` and parsed by the Execution Service.");
            sb.AppendLine("- Do NOT include any text before the JSON — not even one word.");
            sb.AppendLine("- Do NOT wrap the JSON in markdown code fences (no ```json or ```).");
            sb.AppendLine("- Do NOT include any explanation after the JSON.");
            sb.AppendLine("- Your first character must be `{` and your last character must be `}`.");
            sb.AppendLine("- Remove ALL `_comment` and `_template_note` keys from the JSON. These are authoring notes only.");
        }
        else if (agentName == "Analysis")
        {
            sb.AppendLine();
            sb.AppendLine("**ANALYSIS AGENT TOOL BUDGET:**");
            sb.AppendLine("Make NO MORE THAN 10 tool calls total across the entire session. After calling current_position (1 call) and technical_indicators for your primary assets across 2–3 timeframes (up to 8 calls), STOP making tool calls and write analysis.md immediately. You do not need ATR as a separate call — it is not returned by the indicator endpoint. Do not loop fetching the same indicators repeatedly.");
        }
    }

    // ─── File loading ─────────────────────────────────────────────────────────

    private string BuildToolList(string[] toolNames)
    {
        var descriptions = _toolRegistry.GetDescriptions(toolNames);
        if (descriptions.Count == 0)
            return "(no tools available)";

        var sb = new StringBuilder();
        foreach (var (name, description) in descriptions)
            sb.AppendLine($"- **{name}**: {description}");
        return sb.ToString().TrimEnd();
    }

    private static string LoadPromptFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "agent_prompts", fileName);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : $"[Missing prompt file: {fileName}]";
    }

    private static string LoadTemplate(string templateName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "output_templates", templateName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
