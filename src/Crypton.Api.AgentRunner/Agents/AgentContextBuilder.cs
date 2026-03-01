using System.Text;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Mailbox;
using AgentRunner.Tools;

namespace AgentRunner.Agents;

public class AgentContextBuilder
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

    public AgentContext BuildPlanAgentContext(string cycleId, string? previousCycleId = null)
    {
        var identity = LoadAgentIdentity("plan");
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages("plan", _config.Storage.MaxMailboxMessages);
        var memory = _artifactManager.ReadMemory("plan");
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var recentEvaluations = _artifactManager.GetRecentEvaluations(7);
        var outputTemplate = LoadOutputTemplate("plan.md");

        return new AgentContext
        {
            AgentName = "Plan",
            CycleId = cycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            Memory = memory,
            SharedMemory = sharedMemory,
            RecentEvaluations = recentEvaluations,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "web_search", "web_fetch", "bird", "technical_indicators" }
        };
    }

    public AgentContext BuildResearchAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("research");
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages("research", _config.Storage.MaxMailboxMessages);
        var plan = _artifactManager.ReadArtifact(cycleId, "plan.md");
        var memory = _artifactManager.ReadMemory("research");
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var outputTemplate = LoadOutputTemplate("research.md");

        return new AgentContext
        {
            AgentName = "Research",
            CycleId = cycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["plan.md"] = plan ?? ""
            },
            Memory = memory,
            SharedMemory = sharedMemory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "web_search", "web_fetch", "bird", "technical_indicators" }
        };
    }

    public AgentContext BuildAnalysisAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("analysis");
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages("analysis", _config.Storage.MaxMailboxMessages);
        var research = _artifactManager.ReadArtifact(cycleId, "research.md");
        var memory = _artifactManager.ReadMemory("analysis");
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var outputTemplate = LoadOutputTemplate("analysis.md");

        return new AgentContext
        {
            AgentName = "Analysis",
            CycleId = cycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["research.md"] = research ?? ""
            },
            Memory = memory,
            SharedMemory = sharedMemory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position", "technical_indicators" }
        };
    }

    public AgentContext BuildSynthesisAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("synthesis");
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages("synthesis", _config.Storage.MaxMailboxMessages);
        var analysis = _artifactManager.ReadArtifact(cycleId, "analysis.md");
        var memory = _artifactManager.ReadMemory("synthesis");
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var outputTemplate = LoadOutputTemplate("strategy.json");

        return new AgentContext
        {
            AgentName = "Synthesis",
            CycleId = cycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["analysis.md"] = analysis ?? ""
            },
            Memory = memory,
            SharedMemory = sharedMemory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position" }
        };
    }

    /// <summary>
    /// Builds the context for the Evaluation agent.
    /// Evaluation runs as Step 0 of the NEW cycle, so it reads artifacts from the PREVIOUS cycle.
    /// </summary>
    /// <param name="currentCycleId">The ID of the cycle being started (evaluation output is saved here).</param>
    /// <param name="previousCycleId">The ID of the cycle whose artifacts are being evaluated. If null, falls back to currentCycleId.</param>
    public AgentContext BuildEvaluationAgentContext(string currentCycleId, string? previousCycleId = null)
    {
        var sourceCycleId = previousCycleId ?? currentCycleId;
        var identity = LoadAgentIdentity("evaluation");
        var toolGuide = LoadToolGuide();
        var mailbox = _mailboxManager.GetMessages("evaluation", _config.Storage.MaxMailboxMessages);
        var strategy = _artifactManager.ReadArtifact(sourceCycleId, "strategy.json");
        var analysis = _artifactManager.ReadArtifact(sourceCycleId, "analysis.md");
        var sharedMemory = _artifactManager.ReadSharedMemory();
        var recentEvaluations = _artifactManager.GetRecentEvaluations(3);
        var outputTemplate = LoadOutputTemplate("evaluation.md");

        return new AgentContext
        {
            AgentName = "Evaluation",
            CycleId = currentCycleId,
            Identity = identity,
            ToolGuide = toolGuide,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["strategy.json"] = strategy ?? "",
                ["analysis.md"] = analysis ?? "",
                ["evaluated_cycle_id"] = sourceCycleId
            },
            SharedMemory = sharedMemory,
            RecentEvaluations = recentEvaluations,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position" }
        };
    }

    private string LoadAgentIdentity(string agentName)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "agent_prompts", $"{agentName}_agent.md");
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return $"No identity file found for {agentName} agent.";
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
        sb.AppendLine("- `<tool_call>` tags are parsed and executed by the host system. They return REAL data. Do NOT write pseudocode or Python.");
        sb.AppendLine("- You MUST call tools BEFORE writing your output document. Do not assume or invent data.");
        sb.AppendLine("- Write each tool call on its own line, and ALWAYS include the closing tag: `<tool_call>toolname {...}</tool_call>`");
        sb.AppendLine("- Wait for each tool result before proceeding to the next step.");
        sb.AppendLine("- Follow the procedure in your identity instructions step by step.");

        return sb.ToString();
    }
}
