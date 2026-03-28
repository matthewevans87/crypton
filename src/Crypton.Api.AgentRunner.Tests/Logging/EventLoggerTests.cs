using AgentRunner.Abstractions;
using AgentRunner.Domain;
using AgentRunner.Logging;
using System.Text.Json;
using Xunit;

namespace AgentRunner.Tests.Logging;

public class EventLoggerTests : IDisposable
{
    private readonly string _tempPath;
    private readonly string _cyclesPath;
    private readonly string _logPath;

    public EventLoggerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"logger_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);
        _cyclesPath = Path.Combine(_tempPath, "cycles");
        _logPath = Path.Combine(_tempPath, "logs", "test.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    private EventLogger MakeLogger(bool capturePrompts = true) =>
        new(_logPath, _cyclesPath, maxFileSizeMb: 100, maxFileCount: 5, capturePrompts: capturePrompts);

    // ── Basic log methods ────────────────────────────────────────────────────

    [Fact]
    public void LogInfo_AppendsLineToLogFile()
    {
        var logger = MakeLogger();
        logger.LogInfo("hello world");
        var content = File.ReadAllText(_logPath);
        Assert.Contains("[INFO   ] hello world", content);
    }

    [Fact]
    public void LogWarning_AppendsLineToLogFile()
    {
        var logger = MakeLogger();
        logger.LogWarning("watch out");
        var content = File.ReadAllText(_logPath);
        Assert.Contains("[WARN   ] watch out", content);
    }

    [Fact]
    public void LogError_AppendsLineToLogFile()
    {
        var logger = MakeLogger();
        logger.LogError("something broke");
        var content = File.ReadAllText(_logPath);
        Assert.Contains("[ERROR  ] something broke", content);
    }

    [Fact]
    public void LogStateTransition_WritesTextAndStructuredLog()
    {
        var logger = MakeLogger();
        logger.LogStateTransition(LoopState.Idle, LoopState.Plan);
        var text = File.ReadAllText(_logPath);
        Assert.Contains("Idle -> Plan", text);

        var jsonlPath = Path.Combine(_tempPath, "logs", "events.jsonl");
        var jsonl = File.ReadAllText(jsonlPath);
        Assert.Contains("state_transition", jsonl);
    }

    [Fact]
    public void LogAgentInvocation_WritesTextAndStructuredLog()
    {
        var logger = MakeLogger();
        logger.LogAgentInvocation("Plan", "cycle-001");
        var text = File.ReadAllText(_logPath);
        Assert.Contains("Plan", text);
        Assert.Contains("cycle-001", text);
    }

    [Fact]
    public void LogAgentCompletion_Success_WritesCompleted()
    {
        var logger = MakeLogger();
        logger.LogAgentCompletion("Plan", "cycle-001", success: true);
        var text = File.ReadAllText(_logPath);
        Assert.Contains("completed", text);
    }

    [Fact]
    public void LogAgentCompletion_Failure_WritesFailed()
    {
        var logger = MakeLogger();
        logger.LogAgentCompletion("Plan", "cycle-001", success: false);
        var text = File.ReadAllText(_logPath);
        Assert.Contains("failed", text);
    }

    [Fact]
    public void LogToolExecution_WritesToolNameAndDuration()
    {
        var logger = MakeLogger();
        logger.LogToolExecution("web_search", "{\"query\":\"BTC\"}", 1234);
        var text = File.ReadAllText(_logPath);
        Assert.Contains("web_search", text);
        Assert.Contains("1234ms", text);
    }

    [Fact]
    public void LogMailboxDelivery_WritesAgentNames()
    {
        var logger = MakeLogger();
        logger.LogMailboxDelivery("Plan", "Research", "Key signal: BTC rejected at $70k");
        var text = File.ReadAllText(_logPath);
        Assert.Contains("Research", text);
        Assert.Contains("Plan", text);
    }

    [Fact]
    public void LogRetryAttempt_WritesAttemptCount()
    {
        var logger = MakeLogger();
        logger.LogRetryAttempt("Plan", "plan", 2, 3);
        var text = File.ReadAllText(_logPath);
        Assert.Contains("2/3", text);
    }

    // ── Prompt snapshot ──────────────────────────────────────────────────────

    [Fact]
    public void LogPromptSnapshot_WhenCaptureEnabled_WritesSnapshotFile()
    {
        var logger = MakeLogger(capturePrompts: true);
        logger.LogPromptSnapshot("Plan", "cycle-001", "SYSTEM PROMPT", "USER PROMPT");

        var snapshotPath = Path.Combine(_cyclesPath, "cycle-001", "plan_prompt_snapshot.md");
        Assert.True(File.Exists(snapshotPath));

        var content = File.ReadAllText(snapshotPath);
        Assert.Contains("SYSTEM PROMPT", content);
        Assert.Contains("USER PROMPT", content);
        Assert.Contains("cycle-001", content);
    }

    [Fact]
    public void LogPromptSnapshot_WhenCaptureDisabled_DoesNotWriteFile()
    {
        var logger = MakeLogger(capturePrompts: false);
        logger.LogPromptSnapshot("Plan", "cycle-001", "SYSTEM PROMPT", "USER PROMPT");

        var snapshotPath = Path.Combine(_cyclesPath, "cycle-001", "plan_prompt_snapshot.md");
        Assert.False(File.Exists(snapshotPath));
    }

    [Fact]
    public void LogPromptSnapshot_WhenNoCyclesBasePath_DoesNotThrow()
    {
        var logger = new EventLogger(_logPath, cyclesBasePath: null, capturePrompts: true);
        // Should silently no-op rather than throw.
        var ex = Record.Exception(() =>
            logger.LogPromptSnapshot("Plan", "cycle-001", "SYSTEM", "USER"));
        Assert.Null(ex);
    }

    // ── Tool call journal ────────────────────────────────────────────────────

    [Fact]
    public void LogToolCallJournal_AppendsJsonlEntry()
    {
        var logger = MakeLogger(capturePrompts: true);
        logger.LogToolCallJournal("Plan", "cycle-001", 1, "web_search", "{\"query\":\"BTC\"}", "{\"results\":[]}", 250);

        var journalPath = Path.Combine(_cyclesPath, "cycle-001", "plan_tool_log.jsonl");
        Assert.True(File.Exists(journalPath));

        var lines = File.ReadAllLines(journalPath);
        Assert.Single(lines);

        var entry = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("web_search", entry.GetProperty("tool").GetString());
        Assert.Equal(1, entry.GetProperty("iteration").GetInt32());
        Assert.Equal(250, entry.GetProperty("duration_ms").GetInt64());
    }

    [Fact]
    public void LogToolCallJournal_MultipleCalls_AppendsMultipleLines()
    {
        var logger = MakeLogger(capturePrompts: true);
        logger.LogToolCallJournal("Research", "cycle-002", 1, "web_search", "{}", "{}", 100);
        logger.LogToolCallJournal("Research", "cycle-002", 1, "web_fetch", "{}", "{}", 200);
        logger.LogToolCallJournal("Research", "cycle-002", 2, "bird", "{}", "{}", 300);

        var journalPath = Path.Combine(_cyclesPath, "cycle-002", "research_tool_log.jsonl");
        var lines = File.ReadAllLines(journalPath);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void LogToolCallJournal_WhenCaptureDisabled_DoesNotWriteFile()
    {
        var logger = MakeLogger(capturePrompts: false);
        logger.LogToolCallJournal("Plan", "cycle-001", 1, "web_search", "{}", "{}", 100);

        var journalPath = Path.Combine(_cyclesPath, "cycle-001", "plan_tool_log.jsonl");
        Assert.False(File.Exists(journalPath));
    }

    // ── Invocation manifest ──────────────────────────────────────────────────

    [Fact]
    public void LogInvocationManifest_WritesJsonFile()
    {
        var logger = MakeLogger();
        var manifest = new InvocationManifest(
            Model: "qwen3.5:35b",
            Temperature: 0.3,
            NumCtx: 65536,
            IterationsUsed: 5,
            MaxIterations: 50,
            DurationMs: 12000,
            Success: true,
            Error: null);

        logger.LogInvocationManifest("Plan", "cycle-001", manifest);

        var manifestPath = Path.Combine(_cyclesPath, "cycle-001", "plan_run_manifest.json");
        Assert.True(File.Exists(manifestPath));

        var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifestPath));
        Assert.Equal("qwen3.5:35b", json.GetProperty("model").GetString());
        Assert.Equal(0.3, json.GetProperty("temperature").GetDouble(), precision: 9);
        Assert.Equal(5, json.GetProperty("iterations_used").GetInt32());
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("error").ValueKind);
    }

    [Fact]
    public void LogInvocationManifest_OnFailure_WritesErrorField()
    {
        var logger = MakeLogger();
        var manifest = new InvocationManifest(
            Model: "qwen3.5:35b",
            Temperature: 0.3,
            NumCtx: 65536,
            IterationsUsed: 0,
            MaxIterations: 50,
            DurationMs: 500,
            Success: false,
            Error: "Agent execution timed out");

        logger.LogInvocationManifest("Plan", "cycle-err", manifest);

        var manifestPath = Path.Combine(_cyclesPath, "cycle-err", "plan_run_manifest.json");
        var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(manifestPath));
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("Agent execution timed out", json.GetProperty("error").GetString());
    }

    [Fact]
    public void LogInvocationManifest_WhenNoCyclesBasePath_DoesNotThrow()
    {
        var logger = new EventLogger(_logPath, cyclesBasePath: null);
        var manifest = new InvocationManifest("model", 0.1, 65536, 1, 50, 1000, true, null);
        // Should not throw — if it does the test will fail automatically
        logger.LogInvocationManifest("Plan", "cycle-001", manifest);
    }

    [Fact]
    public void LogInvocationManifest_AlsoWritesTextLog()
    {
        var logger = MakeLogger();
        var manifest = new InvocationManifest("qwen3.5:35b", 0.3, 65536, 3, 50, 8000, true, null);
        logger.LogInvocationManifest("Plan", "cycle-001", manifest);

        var text = File.ReadAllText(_logPath);
        Assert.Contains("[MANIFEST]", text);
        Assert.Contains("Plan", text);
    }

    // ── Manifest field naming (snake_case) ───────────────────────────────────

    [Fact]
    public void LogInvocationManifest_UsesSnakeCasePropertyNames()
    {
        var logger = MakeLogger();
        var manifest = new InvocationManifest("m", 0.1, 65536, 1, 50, 100, true, null);
        logger.LogInvocationManifest("Synthesis", "cycle-001", manifest);

        var raw = File.ReadAllText(Path.Combine(_cyclesPath, "cycle-001", "synthesis_run_manifest.json"));
        Assert.Contains("\"num_ctx\"", raw);
        Assert.Contains("\"iterations_used\"", raw);
        Assert.Contains("\"max_iterations\"", raw);
        Assert.Contains("\"duration_ms\"", raw);
    }
}
