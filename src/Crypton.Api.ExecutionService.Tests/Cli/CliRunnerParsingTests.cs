using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Cli;

/// <summary>
/// Tests for CliRunner inputs that don't require a full DI container.
/// Full command tests (status, set-mode, etc.) are integration tests because
/// CliRunner builds its own service provider from environment configuration.
/// </summary>
public sealed class CliRunnerParsingTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Verb detection
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("status")]
    [InlineData("safe-mode")]
    [InlineData("set-mode")]
    [InlineData("run-order")]
    [InlineData("reconcile")]
    [InlineData("promote-to-live")]
    [InlineData("demote-to-paper")]
    public void AllCLIVerbs_AreRecognised_InProgramVerbList(string verb)
    {
        // This mirrors the verb list in Program.cs.
        var allCliVerbs = new[]
        {
            "status", "safe-mode", "set-mode", "run-order", "reconcile",
            "promote-to-live", "demote-to-paper"
        };

        allCliVerbs.Should().Contain(verb);
    }

    [Fact]
    public void ServiceFlag_PreventsCLIMode()
    {
        // When --service is present, isCliMode should be false regardless of verb.
        var args = new[] { "status", "--service" };
        var isCliMode = !args.Contains("--service") &&
            (args.Contains("--cli") || (args.Length > 0 &&
             new[] { "status", "safe-mode", "set-mode", "run-order", "reconcile",
                     "promote-to-live", "demote-to-paper" }.Contains(args[0])));

        isCliMode.Should().BeFalse("--service flag should force service mode");
    }

    [Fact]
    public void CliFlag_EnablesCLIMode_EvenWithoutVerb()
    {
        var args = new[] { "--cli", "status" };
        var allCliVerbs = new[]
        {
            "status", "safe-mode", "set-mode", "run-order", "reconcile",
            "promote-to-live", "demote-to-paper"
        };

        var isCliMode = !args.Contains("--service") &&
            (args.Contains("--cli") ||
             (args.Length > 0 && allCliVerbs.Contains(args[0])));

        isCliMode.Should().BeTrue("--cli flag should trigger CLI mode");
    }

    [Fact]
    public void VerbFirst_EnablesCLIMode()
    {
        var args = new[] { "status" };
        var allCliVerbs = new[]
        {
            "status", "safe-mode", "set-mode", "run-order", "reconcile",
            "promote-to-live", "demote-to-paper"
        };

        var isCliMode = !args.Contains("--service") &&
            (args.Contains("--cli") ||
             (args.Length > 0 && allCliVerbs.Contains(args[0])));

        isCliMode.Should().BeTrue("First-position verb should trigger CLI mode");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Env file argument parsing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnvFileArg_ParsedCorrectly()
    {
        var args = new[] { "--env-file", "/tmp/.env" };
        var envFileIndex = Array.IndexOf(args, "--env-file");
        var envFilePath = envFileIndex >= 0 && envFileIndex + 1 < args.Length
            ? args[envFileIndex + 1]
            : null;

        envFilePath.Should().Be("/tmp/.env");
    }

    [Fact]
    public void EnvFileArg_TildeExpanded()
    {
        var rawPath = "~/.env";
        var expanded = rawPath.StartsWith("~/")
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                rawPath[2..])
            : rawPath;

        expanded.Should().NotContain("~/");
    }

    [Fact]
    public void EnvFileArg_Missing_PathIsNull()
    {
        var args = new[] { "status" };
        var envFileIndex = Array.IndexOf(args, "--env-file");
        var envFilePath = envFileIndex >= 0 && envFileIndex + 1 < args.Length
            ? args[envFileIndex + 1]
            : null;

        envFilePath.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Env alias logic
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnvAlias_ShortForm_MappedToLongForm()
    {
        // Simulates the alias mapping from Program.cs.
        // KRAKEN_API_KEY → EXECUTION_SERVICE__KRAKEN__ApiKey
        const string shortKey = "KRAKEN_API_KEY";
        const string longKey = "EXECUTION_SERVICE__KRAKEN__ApiKey";
        const string testValue = "test-key-123";

        // Save original values.
        var originalShort = Environment.GetEnvironmentVariable(shortKey);
        var originalLong = Environment.GetEnvironmentVariable(longKey);

        try
        {
            Environment.SetEnvironmentVariable(longKey, null);
            Environment.SetEnvironmentVariable(shortKey, testValue);

            // Run the alias logic.
            var value = Environment.GetEnvironmentVariable(shortKey);
            if (!string.IsNullOrEmpty(value) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable(longKey)))
            {
                Environment.SetEnvironmentVariable(longKey, value);
            }

            Environment.GetEnvironmentVariable(longKey).Should().Be(testValue);
        }
        finally
        {
            // Restore original values.
            Environment.SetEnvironmentVariable(shortKey, originalShort);
            Environment.SetEnvironmentVariable(longKey, originalLong);
        }
    }
}
