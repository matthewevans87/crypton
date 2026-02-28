using Crypton.Configuration;
using Xunit;

namespace Crypton.Configuration.Tests;

/// <summary>
/// Tests for DotEnvLoader.Load(FileInfo) and DotEnvLoader.Load(string directory).
///
/// Env vars are process-global state, so each test uses unique variable names derived
/// from a per-test GUID to avoid cross-test interference.  Variables set during a test
/// are always removed in a finally block.
/// </summary>
public class DotEnvLoaderTests : IDisposable
{
    private readonly string _testDir;
    // Tracks every env var set during this test instance so Dispose can clean up.
    private readonly List<string> _trackedKeys = new();

    public DotEnvLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dotenv_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        foreach (var key in _trackedKeys)
            Environment.SetEnvironmentVariable(key, null);

        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    // ────────────────────────────────────────────────────────────────
    // Parsing
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_BasicKeyValue_SetEnvironmentVariable()
    {
        var key = UniqueKey("BASIC");
        WriteEnvFile(_testDir, $"{key}=hello");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("hello", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_MultipleEntries_AllSet()
    {
        var keyA = UniqueKey("A");
        var keyB = UniqueKey("B");
        var keyC = UniqueKey("C");
        WriteEnvFile(_testDir,
            $"{keyA}=alpha",
            $"{keyB}=beta",
            $"{keyC}=gamma");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("alpha", Environment.GetEnvironmentVariable(keyA));
        Assert.Equal("beta", Environment.GetEnvironmentVariable(keyB));
        Assert.Equal("gamma", Environment.GetEnvironmentVariable(keyC));
    }

    [Fact]
    public void Load_ValueWithEqualSign_IncludesEntireRemainder()
    {
        var key = UniqueKey("EQ");
        WriteEnvFile(_testDir, $"{key}=foo=bar=baz");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("foo=bar=baz", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_DoubleQuotedValue_StripsQuotes()
    {
        var key = UniqueKey("DQ");
        WriteEnvFile(_testDir, $"""{key}="quoted value" """);

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("quoted value", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_SingleQuotedValue_StripsQuotes()
    {
        var key = UniqueKey("SQ");
        WriteEnvFile(_testDir, $"{key}='single quoted'");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("single quoted", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_CommentLines_AreIgnored()
    {
        var key = UniqueKey("COMMENT");
        WriteEnvFile(_testDir,
            "# this is a comment",
            $"{key}=real_value",
            "# another comment");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("real_value", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_BlankLines_AreIgnored()
    {
        var key = UniqueKey("BLANK");
        WriteEnvFile(_testDir,
            "",
            $"{key}=value",
            "   ");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("value", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_KeysAndValuesTrimmed_WhitespaceStripped()
    {
        var key = UniqueKey("TRIM");
        WriteEnvFile(_testDir, $"  {key}  =  trimmed  ");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("trimmed", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_EmptyValue_SetsEmptyString()
    {
        var key = UniqueKey("EMPTY");
        WriteEnvFile(_testDir, $"{key}=");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal(string.Empty, Environment.GetEnvironmentVariable(key));
    }

    // ────────────────────────────────────────────────────────────────
    // Precedence: existing env vars must not be overwritten
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ExistingEnvVar_IsNotOverwritten()
    {
        var key = UniqueKey("PREEXIST");
        Environment.SetEnvironmentVariable(key, "original");
        WriteEnvFile(_testDir, $"{key}=should_not_win");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("original", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_NewKeyInFile_IsSet_WhenNoExistingEnvVar()
    {
        var key = UniqueKey("NEW");
        Assert.Null(Environment.GetEnvironmentVariable(key)); // precondition
        WriteEnvFile(_testDir, $"{key}=from_file");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("from_file", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_MixedExistingAndNew_ExistingWins_NewIsSet()
    {
        var existing = UniqueKey("EXISTING");
        var newKey = UniqueKey("NEWKEY");
        Environment.SetEnvironmentVariable(existing, "keep_me");
        WriteEnvFile(_testDir,
            $"{existing}=overwrite_attempt",
            $"{newKey}=from_file");

        DotEnvLoader.Load(new FileInfo(Path.Combine(_testDir, ".env")));

        Assert.Equal("keep_me", Environment.GetEnvironmentVariable(existing));
        Assert.Equal("from_file", Environment.GetEnvironmentVariable(newKey));
    }

    // ────────────────────────────────────────────────────────────────
    // Missing / absent files
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NonExistentFile_DoesNotThrow()
    {
        var missing = new FileInfo(Path.Combine(_testDir, "no_such_file.env"));
        var ex = Record.Exception(() => DotEnvLoader.Load(missing));
        Assert.Null(ex);
    }

    [Fact]
    public void Load_DirectoryWithNoEnvFile_DoesNotThrow()
    {
        // Use a temp directory that contains no .env file and has no parent .env.
        // We use a deeply nested subdir inside _testDir and do NOT create a .env there.
        var subDir = Path.Combine(_testDir, "sub", "deep");
        Directory.CreateDirectory(subDir);

        var ex = Record.Exception(() => DotEnvLoader.Load(subDir));
        Assert.Null(ex);
    }

    // ────────────────────────────────────────────────────────────────
    // Directory walk: finds .env in a parent directory
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_DirectoryString_FindsEnvFileInParent()
    {
        // .env lives in _testDir; Load() is called from a child subdirectory.
        var key = UniqueKey("PARENT");
        WriteEnvFile(_testDir, $"{key}=from_parent");

        var child = Path.Combine(_testDir, "child");
        Directory.CreateDirectory(child);

        DotEnvLoader.Load(child);

        Assert.Equal("from_parent", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_DirectoryString_FileinCurrentDirTakesPriorityOverParent()
    {
        // .env in parent sets KEY=parent_value
        // .env in child  sets KEY=child_value
        // Load(child) should pick up the child file first (closer wins).
        var key = UniqueKey("CLOSEST");
        WriteEnvFile(_testDir, $"{key}=parent_value");

        var child = Path.Combine(_testDir, "child");
        Directory.CreateDirectory(child);
        WriteEnvFile(child, $"{key}=child_value");

        DotEnvLoader.Load(child);

        Assert.Equal("child_value", Environment.GetEnvironmentVariable(key));
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>Creates a unique env var name for this test and registers it for cleanup.</summary>
    private string UniqueKey(string suffix)
    {
        var key = $"DOTENV_TEST_{Guid.NewGuid():N}_{suffix}";
        _trackedKeys.Add(key);
        return key;
    }

    /// <summary>Writes a .env file in <paramref name="dir"/> with one line per entry.</summary>
    private static void WriteEnvFile(string dir, params string[] lines) =>
        File.WriteAllText(Path.Combine(dir, ".env"), string.Join('\n', lines));
}
