using AgentRunner.Configuration;
using AgentRunner.Mailbox;
using AgentRunner.Tools;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class SendMailToolTests : IDisposable
{
    private readonly string _tempPath;
    private readonly MailboxManager _mailboxManager;
    private readonly SendMailTool _tool;

    public SendMailToolTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"sendmail_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        var config = new StorageConfig
        {
            BasePath = _tempPath,
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MaxMailboxMessages = 5
        };
        _mailboxManager = new MailboxManager(config);
        _tool = new SendMailTool(_mailboxManager);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsSendMail()
    {
        Assert.Equal("send_mail", _tool.Name);
    }

    [Fact]
    public void Parameters_RequiresToFromAndMessage()
    {
        var required = _tool.Parameters!.Required;
        Assert.Contains("to", required);
        Assert.Contains("from", required);
        Assert.Contains("message", required);
    }

    // ── Parameter validation ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingTo_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["from"] = "plan",
            ["message"] = "hello"
        });

        Assert.False(result.Success);
        Assert.Contains("to", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_MissingFrom_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "research",
            ["message"] = "hello"
        });

        Assert.False(result.Success);
        Assert.Contains("from", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_MissingMessage_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "research",
            ["from"] = "plan"
        });

        Assert.False(result.Success);
        Assert.Contains("message", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownRecipient_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "oracle",
            ["from"] = "plan",
            ["message"] = "hello"
        });

        Assert.False(result.Success);
        Assert.Contains("oracle", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Successful delivery ───────────────────────────────────────────────────

    [Theory]
    [InlineData("plan")]
    [InlineData("research")]
    [InlineData("analysis")]
    [InlineData("synthesis")]
    [InlineData("evaluation")]
    public async Task ExecuteAsync_ValidRecipient_Succeeds(string recipient)
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = recipient,
            ["from"] = "plan",
            ["message"] = "test message"
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_ValidCall_DepositsMessageInMailbox()
    {
        await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "research",
            ["from"] = "plan",
            ["message"] = "Focus on BTC macro signals."
        });

        var messages = _mailboxManager.GetMessages("research");
        Assert.Single(messages);
        Assert.Equal("plan", messages[0].FromAgent);
        Assert.Equal("research", messages[0].ToAgent);
        Assert.Equal("Focus on BTC macro signals.", messages[0].Content);
    }

    [Fact]
    public async Task ExecuteAsync_RecipientIsCaseInsensitive_Succeeds()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "RESEARCH",
            ["from"] = "Plan",
            ["message"] = "test"
        });

        Assert.True(result.Success);
        var messages = _mailboxManager.GetMessages("research");
        Assert.Single(messages);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsToFromAndMessageInData()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "analysis",
            ["from"] = "research",
            ["message"] = "Momentum is bullish."
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }
}
