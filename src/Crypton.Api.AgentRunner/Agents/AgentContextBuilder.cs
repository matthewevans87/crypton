using System.Text;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Mailbox;
using AgentRunner.StateMachine;
using AgentRunner.Tools;

namespace AgentRunner.Agents;

public interface IAgentContextBuilder
{
    AgentContext BuildContext(LoopState state, string cycleId, string? previousCycleId = null);
}

public class AgentContextBuilder : IAgentContextBuilder
{
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly AgentRunnerConfig _config;

    public AgentContextBuilder(
        ArtifactManager artifactManager,
        MailboxManager mailboxManager,
        ToolRegistry toolRegistry,
        AgentRunnerConfig config)
    {
        _artifactManager = artifactManager;
        _mailboxManager = mailboxManager;
        _toolRegistry = toolRegistry;
        _config = config;
    }

    // Maps each agent state to its static context definition.
    // Evaluation is the only special case: it reads input artifacts from previousCycleId, not cycleId.
    private record AgentContextDefinition(
        string AgentName,
        string PromptFile,
        string TemplateFile,
        string[] InputArtifacts,
        string[] AvailableTools,
        bool IncludeAgentMemory = true,
        bool IncludeRecentEvaluations = false,
        int RecentEvaluationCount = 0);

    private static readonly Dictionary<LoopState, AgentContextDefinition> Definitions = new()
    {
        [LoopState.Plan] = new(
            AgentName: "Plan",
            PromptFile: "plan_agent.md",
            TemplateFile: "plan.md",
            InputArtifacts: [],
            AvailableTools: ["web_search", "web_fetch", "bird", "technical_indicators"],
            IncludeRecentEvaluations: true,
            RecentEvaluationCount: 7),

        [LoopState.Research] = new(
            AgentName: "Research",
            PromptFile: "research_agent.md",
            TemplateFile: "research.md",
            InputArtifacts: ["plan.md"],
            AvailableTools: ["web_search", "web_fetch", "bird", "technical_indicators"]),

        [LoopState.Analyze] = new(
            AgentName: "Analysis",
            PromptFile: "analysis_agent.md",
            TemplateFile: "analysis.md",
            InputArtifacts: ["research.md"],
            AvailableTools: ["current_position", "technical_indicators"]),

        [LoopState.Synthesize] = new(
            AgentName: "Synthesis",
            PromptFile: "synthesis_agent.md",
            TemplateFile: "strategy.json",
            InputArtifacts: ["analysis.md"],
            AvailableTools: ["current_position"]),

        [LoopState.Evaluate] = new(
            AgentName: "Evaluation",
            PromptFile: "evaluation_agent.md",
            TemplateFile: "evaluation.md",
            InputArtifacts: ["strategy.json", "analysis.md"],
            AvailableTools: ["current_position"],
            IncludeAgentMemory: false,
            IncludeRecentEvaluations: true,
            RecentEvaluationCount: 3),
    };

    /// <summary>
    /// Builds the agent context for the given loop state. For the Evaluation step,
    /// <paramref name="previousCycleId"/> identifies the cycle whose artifacts are assessed;
    /// the resulting evaluation artifact is saved under <paramref name="cycleId"/>.
    /// </summary>
    public AgentContext BuildContext(LoopState state, string cycleId, string? previousCycleId = null)
    {
        if (!Definitions.TryGetValue(state, out var def))
            throw new InvalidOperationException($"No agent context definition for state: {state}");

        // Evaluation reads its inputs from the previous cycle; all others use the current cycle.
        var sourceCycleId = (state == LoopState.Evaluate)
            ? (previousCycleId ?? cycleId)
            : cycleId;

        var identity = LoadAgentIdentity(def.PromptFile);
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages(def.AgentName.ToLowerInvariant(),
            _config.Storage.MaxMailboxMessages);
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var outputTemplate = LoadOutputTemplate(def.TemplateFile);

        var inputArtifacts = def.InputArtifacts
            .ToDictionary(name => name, name => _artifactManager.ReadArtifact(sourceCycleId, name) ?? "");

        if (state == LoopState.Evaluate)
            inputArtifacts["evaluated_cycle_id"] = sourceCycleId;

        return new AgentContext
        {
            AgentName = def.AgentName,
            CycleId = cycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            Memory = def.IncludeAgentMemory
                ? _artifactManager.ReadMemory(def.AgentName.ToLowerInvariant())
                : null,
            SharedMemory = sharedMemory,
            RecentEvaluations = def.IncludeRecentEvaluations
                ? _artifactManager.GetRecentEvaluations(def.RecentEvaluationCount)
                : [],
            InputArtifacts = inputArtifacts,
            OutputTemplate = outputTemplate,
            AvailableTools = def.AvailableTools,
        };
    }

    private string LoadAgentIdentity(string promptFile)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "agent_prompts", promptFile);
        if (File.Exists(path))
            return File.ReadAllText(path);
        return $"No identity file found: {promptFile}.";
    }

    private string LoadOutputTemplate(string templateName)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "output_templates", templateName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return "";
    }

    private string LoadToolGuide()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "agent_prompts", "tools.md");
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return "# Tool Reference\nNo tools.md found. Use <tool_call>tool_name {\"param\": \"value\"}</tool_call> syntax.";
    }
}

public class AgentContext
{
    public string AgentName { get; set; } = string.Empty;
    public string CycleId { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    /// <summary>Prose tool guide (tools.md) — calling convention + all parameter specs.</summary>
    public string ToolGuide { get; set; } = string.Empty;
    public List<MailboxMessage> MailboxMessages { get; set; } = new();
    public string? Memory { get; set; }
    public string? SharedMemory { get; set; }
    public List<string> RecentEvaluations { get; set; } = new();
    public Dictionary<string, string> InputArtifacts { get; set; } = new();
    public string OutputTemplate { get; set; } = string.Empty;
    public string[] AvailableTools { get; set; } = Array.Empty<string>();

    public string ToPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Agent Identity");
        sb.AppendLine(Identity);
        sb.AppendLine();

        sb.AppendLine("# Tool Reference");
        sb.AppendLine(ToolGuide);
        sb.AppendLine();

        if (MailboxMessages.Any())
        {
            sb.AppendLine("# Mailbox Messages");
            foreach (var msg in MailboxMessages)
            {
                sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm}] From {msg.FromAgent}: {msg.Content}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Memory))
        {
            sb.AppendLine("# Memory");
            sb.AppendLine(Memory);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(SharedMemory))
        {
            sb.AppendLine("# Shared Memory (Cross-Cycle Context)");
            sb.AppendLine(SharedMemory);
            sb.AppendLine();
        }

        if (RecentEvaluations.Any())
        {
            sb.AppendLine("# Recent Evaluations");
            for (int i = 0; i < RecentEvaluations.Count; i++)
            {
                sb.AppendLine($"## Evaluation {i + 1}");
                sb.AppendLine(RecentEvaluations[i]);
            }
            sb.AppendLine();
        }

        if (InputArtifacts.Any())
        {
            sb.AppendLine("# Input Artifacts");
            foreach (var (name, content) in InputArtifacts.Where(kv => kv.Key != "evaluated_cycle_id"))
            {
                sb.AppendLine($"## {name}");
                sb.AppendLine(content);
            }
            sb.AppendLine();
        }

        sb.AppendLine("# Output Template");
        sb.AppendLine(OutputTemplate);

        return sb.ToString();
    }

    /// <summary>
    /// Returns the part of the prompt that should be sent as the 'system' role message —
    /// the stable agent identity and tool guide. This primes the model with WHO it is and
    /// HOW to call tools before any task context is introduced.
    /// </summary>
    public string ToSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Agent Identity");
        sb.AppendLine(Identity);
        sb.AppendLine();

        sb.AppendLine("# Tool Reference");
        sb.AppendLine(ToolGuide);

        return sb.ToString();
    }

    /// <summary>
    /// Returns the part of the prompt that should be sent as the 'user' role message —
    /// the current task: mailbox, memory, artifacts, output template, and the BEGIN trigger.
    /// </summary>
    public string ToUserPrompt()
    {
        var sb = new StringBuilder();

        if (MailboxMessages.Any())
        {
            sb.AppendLine("# Mailbox Messages");
            foreach (var msg in MailboxMessages)
            {
                sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm}] From {msg.FromAgent}: {msg.Content}");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Memory))
        {
            sb.AppendLine("# Memory");
            sb.AppendLine(Memory);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(SharedMemory))
        {
            sb.AppendLine("# Shared Memory (Cross-Cycle Context)");
            sb.AppendLine(SharedMemory);
            sb.AppendLine();
        }

        if (RecentEvaluations.Any())
        {
            sb.AppendLine("# Recent Evaluations");
            for (int i = 0; i < RecentEvaluations.Count; i++)
            {
                sb.AppendLine($"## Evaluation {i + 1}");
                sb.AppendLine(RecentEvaluations[i]);
            }
            sb.AppendLine();
        }

        if (InputArtifacts.Any())
        {
            sb.AppendLine("# Input Artifacts");
            foreach (var (name, content) in InputArtifacts.Where(kv => kv.Key != "evaluated_cycle_id"))
            {
                sb.AppendLine($"## {name}");
                sb.AppendLine(content);
            }
            sb.AppendLine();
        }

        sb.AppendLine("# Output Template");
        sb.AppendLine(OutputTemplate);
        sb.AppendLine();

        // Explicit BEGIN trigger — makes it unambiguous that the agent must now ACT
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
        sb.AppendLine("- **STOP at the end of your document.** The '---' separator above and the text 'BEGIN your work now.' are system triggers, NOT part of your output document. Do NOT copy the phrase 'BEGIN your work now.' anywhere in your output. Your document must end at its natural conclusion (e.g., the Emerging Signals section for analysis.md, or the last mailbox step).");

        // Agent-specific guidance
        if (AgentName == "Synthesis")
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
        else if (AgentName == "Analysis")
        {
            sb.AppendLine();
            sb.AppendLine("**ANALYSIS AGENT TOOL BUDGET:**");
            sb.AppendLine("Make NO MORE THAN 10 tool calls total across the entire session. After calling current_position (1 call) and technical_indicators for your primary assets across 2–3 timeframes (up to 8 calls), STOP making tool calls and write analysis.md immediately. You do not need ATR as a separate call — it is not returned by the indicator endpoint. Do not loop fetching the same indicators repeatedly.");
        }
        else if (AgentName == "Plan")
        {
            sb.AppendLine();
            sb.AppendLine("**PLAN AGENT TOOL BUDGET:**");
            sb.AppendLine("Make NO MORE THAN 12 tool calls total. Once you have gathered data from web_search (3–4 calls), technical_indicators for BTC and ETH (2 calls), on-chain sources (1–2 calls), and congressional/political signals (1–2 calls), STOP making tool calls and write plan.md immediately. Do not keep searching for more data — you will never have complete information. Write the document with what you have.");
        }

        return sb.ToString();
    }
}
