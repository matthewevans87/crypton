using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Execution.Tools;
using AgentRunner.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class SendMailToolTests : IDisposable
{
    private readonly string _tempPath;
    private readonly FileMailboxService _mailbox;
    private readonly SendMailTool _tool;
    private AIFunction? _fn;

    public SendMailToolTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"sendmail_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);
        _mailbox = new FileMailboxService(new StorageConfig
        {
            BasePath = _tempPath,
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MaxMailboxMessages = 5
        });
        _tool = new SendMailTool(_mailbox);
        _fn = _tool.AsAIFunction();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void Name_IsSendMail()
    {
        Assert.Equal("send_mail", _tool.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRecipient_ReturnsConfirmation()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["to"] = "research",
            ["from"] = "plan",
            ["message"] = "test message"
        });
        var result = (await _fn!.InvokeAsync(args))?.ToString();
        Assert.Contains("delivered", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownRecipient_ReturnsError()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["to"] = "oracle",
            ["from"] = "plan",
            ["message"] = "test"
        });
        var result = (await _fn!.InvokeAsync(args))?.ToString();
        Assert.Contains("oracle", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ValidCall_DepositsToMailbox()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["to"] = "research",
            ["from"] = "plan",
            ["message"] = "Focus on BTC."
        });
        await _fn!.InvokeAsync(args);

        var messages = _mailbox.GetMessages("research", 10);
        Assert.Single(messages);
        Assert.Equal("plan", messages[0].FromAgent);
        Assert.Equal("research", messages[0].ToAgent);
        Assert.Equal("Focus on BTC.", messages[0].Content);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("research")]
    [InlineData("analysis")]
    [InlineData("synthesis")]
    [InlineData("evaluation")]
    public async Task ExecuteAsync_AllValidRecipients_Succeed(string recipient)
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["to"] = recipient,
            ["from"] = "plan",
            ["message"] = "test"
        });
        var result = (await _fn!.InvokeAsync(args))?.ToString();
        Assert.DoesNotContain("Error:", result, StringComparison.OrdinalIgnoreCase);
    }
}
