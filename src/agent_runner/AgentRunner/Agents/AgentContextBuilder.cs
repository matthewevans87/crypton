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
        var tools = _toolRegistry.GetToolDescriptionsJson();
        var mailbox = _mailboxManager.GetMessages("plan", _config.Storage.MaxMailboxMessages);
        var memory = _artifactManager.ReadMemory("plan");
        var recentEvaluations = _artifactManager.GetRecentEvaluations(7);
        var outputTemplate = LoadOutputTemplate("plan.md");

        return new AgentContext
        {
            AgentName = "Plan",
            CycleId = cycleId,
            Identity = identity,
            Tools = tools,
            MailboxMessages = mailbox,
            Memory = memory,
            RecentEvaluations = recentEvaluations,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "web_search", "web_fetch", "bird", "technical_indicators" }
        };
    }

    public AgentContext BuildResearchAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("research");
        var tools = _toolRegistry.GetToolDescriptionsJson();
        var mailbox = _mailboxManager.GetMessages("research", _config.Storage.MaxMailboxMessages);
        var plan = _artifactManager.ReadArtifact(cycleId, "plan.md");
        var memory = _artifactManager.ReadMemory("research");
        var outputTemplate = LoadOutputTemplate("research.md");

        return new AgentContext
        {
            AgentName = "Research",
            CycleId = cycleId,
            Identity = identity,
            Tools = tools,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["plan.md"] = plan ?? ""
            },
            Memory = memory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "web_search", "web_fetch", "bird", "technical_indicators" }
        };
    }

    public AgentContext BuildAnalysisAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("analysis");
        var tools = _toolRegistry.GetToolDescriptionsJson();
        var mailbox = _mailboxManager.GetMessages("analysis", _config.Storage.MaxMailboxMessages);
        var research = _artifactManager.ReadArtifact(cycleId, "research.md");
        var memory = _artifactManager.ReadMemory("analysis");
        var outputTemplate = LoadOutputTemplate("analysis.md");

        return new AgentContext
        {
            AgentName = "Analysis",
            CycleId = cycleId,
            Identity = identity,
            Tools = tools,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["research.md"] = research ?? ""
            },
            Memory = memory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position", "technical_indicators" }
        };
    }

    public AgentContext BuildSynthesisAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("synthesis");
        var tools = _toolRegistry.GetToolDescriptionsJson();
        var mailbox = _mailboxManager.GetMessages("synthesis", _config.Storage.MaxMailboxMessages);
        var analysis = _artifactManager.ReadArtifact(cycleId, "analysis.md");
        var memory = _artifactManager.ReadMemory("synthesis");
        var outputTemplate = LoadOutputTemplate("strategy.json");

        return new AgentContext
        {
            AgentName = "Synthesis",
            CycleId = cycleId,
            Identity = identity,
            Tools = tools,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["analysis.md"] = analysis ?? ""
            },
            Memory = memory,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position" }
        };
    }

    public AgentContext BuildEvaluationAgentContext(string cycleId)
    {
        var identity = LoadAgentIdentity("evaluation");
        var tools = _toolRegistry.GetToolDescriptionsJson();
        var mailbox = _mailboxManager.GetMessages("evaluation", _config.Storage.MaxMailboxMessages);
        var strategy = _artifactManager.ReadArtifact(cycleId, "strategy.json");
        var analysis = _artifactManager.ReadArtifact(cycleId, "analysis.md");
        var recentEvaluations = _artifactManager.GetRecentEvaluations(3);
        var outputTemplate = LoadOutputTemplate("evaluation.md");

        return new AgentContext
        {
            AgentName = "Evaluation",
            CycleId = cycleId,
            Identity = identity,
            Tools = tools,
            MailboxMessages = mailbox,
            InputArtifacts = new Dictionary<string, string>
            {
                ["strategy.json"] = strategy ?? "",
                ["analysis.md"] = analysis ?? ""
            },
            RecentEvaluations = recentEvaluations,
            OutputTemplate = outputTemplate,
            AvailableTools = new[] { "current_position" }
        };
    }

    private string LoadAgentIdentity(string agentName)
    {
        var path = Path.Combine("..", "..", "agent_prompts", $"{agentName}_agent.md");
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return $"No identity file found for {agentName} agent.";
    }

    private string LoadOutputTemplate(string templateName)
    {
        var path = Path.Combine("..", "..", "output_templates", templateName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return "";
    }
}

public class AgentContext
{
    public string AgentName { get; set; } = string.Empty;
    public string CycleId { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string Tools { get; set; } = string.Empty;
    public List<MailboxMessage> MailboxMessages { get; set; } = new();
    public string? Memory { get; set; }
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
        
        sb.AppendLine("# Available Tools");
        sb.AppendLine(Tools);
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
            foreach (var (name, content) in InputArtifacts)
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
}
