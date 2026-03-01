using System.Net.Http;
using System.Text.Json;
using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.StateMachine;

namespace AgentRunner.Cli;

/// <summary>
/// CLI debug mode for AgentRunner.
///
/// Usage:
///   dotnet run -- --cli run-step --step plan [--cycle-id 20250101_120000] [--verbose]
///   dotnet run -- --cli run-step --step evaluate [--cycle-id 20250101_120000] [--prev-cycle-id 20250101_060000] [--verbose]
///   dotnet run -- --cli run-cycle [--from plan|evaluate] [--verbose]
///   dotnet run -- --cli status
///
/// Prints full prompts and LLM responses to stdout when --verbose is set.
/// </summary>
public static class CliRunner
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task RunAsync(
        string[] args,
        AgentRunnerConfig config,
        ArtifactManager artifactManager,
        AgentContextBuilder contextBuilder,
        AgentInvoker agentInvoker)
    {
        var verbose = args.Contains("--verbose");
        var command = GetArg(args, "--cli") ?? "help";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{'═',-60}");
        Console.WriteLine($" Crypton AgentRunner — CLI Mode  [{command}]");
        Console.WriteLine($"{'═',-60}");
        Console.ResetColor();

        // Confirm Ollama is reachable before doing real work
        if (command is "run-step" or "run-cycle")
        {
            await CheckOllamaAsync(config.Ollama.BaseUrl);
        }

        switch (command)
        {
            case "run-step":
                await RunStepCommand(args, config, artifactManager, contextBuilder, agentInvoker, verbose);
                break;

            case "run-cycle":
                await RunCycleCommand(args, config, artifactManager, contextBuilder, agentInvoker, verbose);
                break;

            case "status":
                RunStatusCommand(artifactManager);
                break;

            default:
                PrintHelp();
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    private static async Task RunStepCommand(
        string[] args,
        AgentRunnerConfig config,
        ArtifactManager artifactManager,
        AgentContextBuilder contextBuilder,
        AgentInvoker agentInvoker,
        bool verbose)
    {
        var stepName = GetArg(args, "--step");
        if (stepName == null)
        {
            Console.Error.WriteLine("ERROR: --step is required. Valid steps: plan, research, analyze, synthesize, evaluate");
            return;
        }

        if (!Enum.TryParse<LoopState>(stepName, ignoreCase: true, out var state)
            || state is LoopState.Idle or LoopState.Paused or LoopState.Failed or LoopState.WaitingForNextCycle)
        {
            Console.Error.WriteLine($"ERROR: Invalid step '{stepName}'. Valid steps: plan, research, analyze, synthesize, evaluate");
            return;
        }

        var cycleId = GetArg(args, "--cycle-id") ?? artifactManager.CreateCycleDirectory();
        var prevCycleId = GetArg(args, "--prev-cycle-id") ?? artifactManager.GetLatestCompletedCycleId();

        Console.WriteLine($"[CLI] Step     : {state}");
        Console.WriteLine($"[CLI] Cycle ID : {cycleId}");
        if (state == LoopState.Evaluate)
            Console.WriteLine($"[CLI] Prev Cycle: {prevCycleId ?? "(none)"}");

        AgentContext context = state switch
        {
            LoopState.Plan => contextBuilder.BuildPlanAgentContext(cycleId),
            LoopState.Research => contextBuilder.BuildResearchAgentContext(cycleId),
            LoopState.Analyze => contextBuilder.BuildAnalysisAgentContext(cycleId),
            LoopState.Synthesize => contextBuilder.BuildSynthesisAgentContext(cycleId),
            LoopState.Evaluate => contextBuilder.BuildEvaluationAgentContext(cycleId, prevCycleId),
            _ => throw new InvalidOperationException($"No context builder for {state}")
        };

        var systemPrompt = context.ToSystemPrompt();
        var userPrompt = context.ToUserPrompt();
        var tokenEstimate = (systemPrompt.Length + userPrompt.Length) / 4;

        if (verbose)
        {
            PrintSection("SYSTEM PROMPT", systemPrompt);
            PrintSection("USER PROMPT", userPrompt);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Prompt: ~{tokenEstimate:N0} tokens  (--verbose to print)");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [{state}] calling {config.Ollama.BaseUrl} ...");
        Console.ResetColor();

        // Streaming callbacks
        Action<string>? onToken = verbose
            ? token => { Console.ForegroundColor = ConsoleColor.White; Console.Write(token); Console.ResetColor(); }
            : null;

        Action<string> onEvent = msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"\n  {msg}");
            Console.ResetColor();
        };

        if (verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n{'─',-60}  LLM OUTPUT\n");
            Console.ResetColor();
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.Ollama.TimeoutSeconds));
        var result = await agentInvoker.InvokeAsync(context, cts.Token, onToken, onEvent);
        stopwatch.Stop();

        if (verbose) Console.WriteLine(); // newline after streaming tokens

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Done in {stopwatch.Elapsed.TotalSeconds:F1}s  |  iterations={result.Iterations}  |  tool calls={result.ToolCalls.Count}  |  success={result.Success}");
        Console.ResetColor();

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[CLI] ERROR: {result.Error}");
            Console.ResetColor();
            return;
        }

        if (verbose)
        {
            PrintSection("RESPONSE", result.Output ?? "(empty)");
        }

        // Save artifact
        var artifactName = state switch
        {
            LoopState.Plan => "plan.md",
            LoopState.Research => "research.md",
            LoopState.Analyze => "analysis.md",
            LoopState.Synthesize => "strategy.json",
            LoopState.Evaluate => "evaluation.md",
            _ => throw new InvalidOperationException()
        };

        var outputToSave = artifactName.EndsWith(".json")
            ? SanitizeJsonOutput(result.Output ?? "", artifactName)
            : result.Output ?? "";
        artifactManager.SaveArtifact(cycleId, artifactName, outputToSave);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[CLI] Artifact saved: {artifactManager.GetArtifactPath(cycleId, artifactName)}");
        Console.ResetColor();

        if (!verbose && result.Output != null)
        {
            var preview = result.Output.Length > 2000
                ? result.Output[..2000] + $"\n\n  ... [{result.Output.Length - 2000} more chars — use --verbose for full output]"
                : result.Output;
            PrintSection($"OUTPUT ({result.Output.Length:N0} chars)", preview);
        }
    }

    private static async Task RunCycleCommand(
        string[] args,
        AgentRunnerConfig config,
        ArtifactManager artifactManager,
        AgentContextBuilder contextBuilder,
        AgentInvoker agentInvoker,
        bool verbose)
    {
        var fromStep = GetArg(args, "--from") ?? "plan";
        var cycleId = artifactManager.CreateCycleDirectory();
        var prevCycleId = artifactManager.GetLatestCompletedCycleId();

        Console.WriteLine($"[CLI] Running full cycle: {cycleId}");
        Console.WriteLine($"[CLI] Starting from: {fromStep}");

        var steps = new List<LoopState> { LoopState.Plan, LoopState.Research, LoopState.Analyze, LoopState.Synthesize };

        if (fromStep.Equals("evaluate", StringComparison.OrdinalIgnoreCase) && prevCycleId != null)
        {
            steps.Insert(0, LoopState.Evaluate);
        }
        else if (fromStep.Equals("evaluate", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[CLI] Skipping evaluate — no previous completed cycle found.");
        }

        foreach (var state in steps)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[CLI] === Step: {state} ===");
            Console.ResetColor();

            var stepArgs = new[] { "--cli", "run-step", "--step", state.ToString().ToLower(),
                "--cycle-id", cycleId };

            if (prevCycleId != null)
                stepArgs = stepArgs.Concat(new[] { "--prev-cycle-id", prevCycleId }).ToArray();

            if (verbose)
                stepArgs = stepArgs.Append("--verbose").ToArray();

            await RunStepCommand(stepArgs, config, artifactManager, contextBuilder, agentInvoker, verbose);

            // After synthesize, this becomes the "previous" for any subsequent evaluation
            if (state == LoopState.Synthesize && artifactManager.ArtifactExists(cycleId, "strategy.json"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[CLI] Cycle {cycleId} completed successfully.");
                Console.ResetColor();
            }
        }
    }

    private static void RunStatusCommand(ArtifactManager artifactManager)
    {
        var latestCycleId = artifactManager.GetLatestCompletedCycleId();
        var recentCycles = artifactManager.GetRecentCycles(5);

        Console.WriteLine($"[CLI] Latest completed cycle : {latestCycleId ?? "(none)"}");
        Console.WriteLine($"[CLI] Recent cycles ({recentCycles.Count}):");
        foreach (var c in recentCycles)
        {
            var artifacts = Directory.GetFiles(artifactManager.GetCycleDirectory(c))
                .Select(Path.GetFileName)
                .OrderBy(x => x)
                .ToList();
            Console.WriteLine($"  {c}  [{string.Join(", ", artifacts)}]");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            USAGE: dotnet run -- --cli <command> [options]

            GLOBAL OPTIONS:
              --env-file <path>       Load environment variables from a specific .env file
                                      (default: ~/.config/crypton/.env, then walk up from cwd)
                                      Supports ~ expansion. Real env vars are never overwritten.

            COMMANDS:
              run-step   Run a single agent step
                --step <plan|research|analyze|synthesize|evaluate>  (required)
                --cycle-id <id>         Cycle directory to use (default: creates new)
                --prev-cycle-id <id>    Source cycle for evaluate step (default: latest completed)
                --verbose               Print full prompts and responses

              run-cycle  Run a full cycle (all steps in order)
                --from <plan|evaluate>  Start from evaluate if history exists (default: plan)
                --verbose               Print full prompts and responses

              status     Show recent cycle history and artifact summary

            EXAMPLES:
              dotnet run -- --cli run-step --step plan --verbose
              dotnet run -- --cli run-step --step plan --env-file ~/.config/crypton/.env --verbose
              dotnet run -- --cli run-step --step evaluate --prev-cycle-id 20250601_120000
              dotnet run -- --cli run-cycle --from evaluate --verbose
              dotnet run -- --cli status
            """);
    }

    // -------------------------------------------------------------------------
    // Ollama connectivity check
    // -------------------------------------------------------------------------

    private static async Task CheckOllamaAsync(string baseUrl)
    {
        Console.Write($"  Checking Ollama at {baseUrl} ... ");
        try
        {
            var resp = await _http.GetAsync(baseUrl.TrimEnd('/') + "/api/tags");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var models = doc.RootElement.GetProperty("models")
                    .EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString())
                    .ToList();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"OK  ({models.Count} model(s))");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Models: {string.Join(", ", models.Take(5))}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"HTTP {resp.StatusCode} — continuing");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.Error.WriteLine($"  ERROR: Cannot reach Ollama: {ex.Message}");
            Console.Error.WriteLine($"  Ensure Ollama is running: ollama serve");
            Console.ResetColor();
            Environment.Exit(1);
        }
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        if (idx < 0) return null;
        // Allow "--cli run-step" (value is next arg) or if flag itself is the value (no next arg)
        if (idx + 1 < args.Length && !args[idx + 1].StartsWith("--"))
            return args[idx + 1];
        return null;
    }

    /// <summary>
    /// Extracts the JSON object from an LLM response that may contain prose or code-fence wrappers.
    /// Priority: 1) ```json...``` block, 2) first '{' to last '}', 3) original string.
    /// </summary>
    private static string SanitizeJsonOutput(string output, string artifactName)
    {
        var json = ExtractJsonFromOutput(output);

        // Safety backstop: strategy.json must always use paper mode
        if (artifactName == "strategy.json" && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("mode", out var modeEl) &&
                    modeEl.GetString() != "paper")
                {
                    using var ms = new System.IO.MemoryStream();
                    using (var writer = new System.Text.Json.Utf8JsonWriter(ms,
                        new System.Text.Json.JsonWriterOptions { Indented = true }))
                    {
                        writer.WriteStartObject();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Name == "mode")
                                writer.WriteString("mode", "paper");
                            else
                                prop.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    return System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch
            {
                // Not valid JSON yet — return as-is; downstream schema validation will catch it
            }
        }

        return json;
    }

    private static string ExtractJsonFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return output;

        // Try to find a ```json ... ``` code fence
        var fenceStart = output.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart >= 0)
        {
            var contentStart = output.IndexOf('\n', fenceStart) + 1;
            var fenceEnd = output.IndexOf("```", contentStart);
            if (fenceEnd > contentStart)
                return output[contentStart..fenceEnd].Trim();
        }

        // Try plain ``` code fence
        var plainFence = output.IndexOf("```\n{");
        if (plainFence >= 0)
        {
            var contentStart = output.IndexOf('{', plainFence);
            var fenceEnd = output.IndexOf("```", contentStart);
            if (fenceEnd > contentStart)
                return output[contentStart..fenceEnd].Trim();
        }

        // Try to extract from first '{' to last '}'
        var braceStart = output.IndexOf('{');
        var braceEnd = output.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            return output[braceStart..(braceEnd + 1)].Trim();

        return output;
    }

    private static void PrintSection(string title, string content)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n{'─',-80}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($" {title}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{'─',-80}");
        Console.ResetColor();
        Console.WriteLine(content);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{'─',-80}\n");
        Console.ResetColor();
    }
}
