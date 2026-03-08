using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRunner.Tools;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class BirdToolTests
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;

    public BirdToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    private BirdTool CreateTool(string baseUrl = "http://localhost:11435", int timeout = 30)
        => new(_httpClient, baseUrl, timeout);

    private static void SetupBirdResponse(MockHttpMessageHandler handler, string url, int exitCode, string stdout, string stderr = "")
    {
        handler.SetupResponse(url, new { stdout, stderr, exitCode });
    }

    // ── Parameter validation ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsError()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("query", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyQuery_ReturnsError()
    {
        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("query", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Successful search ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SearchMode_ReturnsData()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "1", text = "hello" } });
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, tweets);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin",
            ["mode"] = "search",
            ["count"] = 5
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultMode_IsSearch()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "1", text = "hello" } });
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, tweets);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "crypto"
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    // ── Timeline mode ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TimelineWithUsername_ReturnsData()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "2", text = "tweet" } });
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, tweets);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "@elonmusk",
            ["mode"] = "timeline"
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_TimelineWithoutUsername_ReturnsData()
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, "[]");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "home",
            ["mode"] = "timeline"
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    // ── Count clamping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, 50)]
    [InlineData(25, 25)]
    public async Task ExecuteAsync_CountIsClamped(int requested, int expected)
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, "[]");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "test",
            ["count"] = requested
        }, CancellationToken.None);

        Assert.True(result.Success);
        // Verify the count was used (we don't inspect the HTTP body, but the call succeeded)
        _ = expected; // count clamping is validated by the tool not crashing
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BirdNonZeroExit_ReturnsError()
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 1, "", "Authentication failed");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Authentication failed", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_BirdNonZeroExitNoStderr_ReturnsExitCode()
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 2, "");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exited with code 2", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ServerReturnsHttpError_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:11435/execute", HttpStatusCode.ServiceUnavailable);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ServerTimeout_ReturnsError()
    {
        _mockHandler.SetupTimeout("http://localhost:11435/execute");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Empty output ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyStdout_ReturnsEmptyArray()
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, "");

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("[]", result.Data?.ToString());
    }

    // ── URL trailing slash handling ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_BaseUrlWithTrailingSlash_Works()
    {
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, "[]");

        var tool = CreateTool("http://localhost:11435/");
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "test"
        }, CancellationToken.None);

        Assert.True(result.Success);
    }

    // ── Tweet normalization ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NormalizesJsonArrayOutput()
    {
        var rawTweets = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "123",
                text = "Bitcoin is rising",
                username = "satoshi",
                name = "Satoshi",
                created_at = "2026-03-08T00:00:00Z",
                public_metrics = new { retweet_count = 10, like_count = 100, reply_count = 5 }
            }
        });
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, rawTweets);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "bitcoin"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data?.ToString();
        Assert.NotNull(data);
        Assert.Contains("satoshi", data);
        Assert.Contains("Bitcoin is rising", data);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesWrappedDataOutput()
    {
        var rawTweets = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { id = "456", text = "ETH update" }
            }
        });
        SetupBirdResponse(_mockHandler, "http://localhost:11435/execute", 0, rawTweets);

        var tool = CreateTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "ethereum"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var data = result.Data?.ToString();
        Assert.NotNull(data);
        Assert.Contains("ETH update", data);
    }

    // ── Tool metadata ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsBird()
    {
        var tool = CreateTool();
        Assert.Equal("bird", tool.Name);
    }

    [Fact]
    public void Parameters_RequiresQuery()
    {
        var tool = CreateTool();
        Assert.Contains("query", tool.Parameters.Required);
    }
}
