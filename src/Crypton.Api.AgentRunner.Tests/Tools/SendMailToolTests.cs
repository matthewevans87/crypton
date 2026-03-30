using Crypton.Tools;
using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Spectre.Console.Cli;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class SendMailToolTests : IDisposable
{
    private static readonly ToolsConfig Config = new() { MaxRetries = 0, DefaultTimeoutSeconds = 30 };

    private readonly string _tempPath;
    private readonly FileMailboxService _mailbox;
    private readonly (ToolRegistry Registry, InProcessVirtualTerminal Vt) _vt;

    public SendMailToolTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"sendmail_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);
        _mailbox = new FileMailboxService(Path.Combine(_tempPath, "mailboxes"), 5);

        var registry = new ToolRegistry();
        registry.Register<SendMailTool, SendMailTool.Settings>(
            "send_mail",
            console =>
            {
                var tool = new SendMailTool(_mailbox, console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(SendMailTool), tool);
                var app = new CommandApp<SendMailTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            });
        _vt = (registry, new InProcessVirtualTerminal(registry, Config));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public async Task RunAsync_ValidRecipient_ReturnsSuccess()
    {
        var result = await _vt.Vt.RunAsync("send_mail",
            "{\"to\": \"research\", \"from\": \"plan\", \"message\": \"test message\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.Contains("delivered", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_UnknownRecipient_ReturnsFailure()
    {
        var result = await _vt.Vt.RunAsync("send_mail",
            "{\"to\": \"oracle\", \"from\": \"plan\", \"message\": \"test\"}");

        Assert.False(result.IsSuccess);
        Assert.Contains("oracle", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ValidCall_DepositsToMailbox()
    {
        await _vt.Vt.RunAsync("send_mail",
            "{\"to\": \"research\", \"from\": \"plan\", \"message\": \"Focus on BTC.\"}");

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
    public async Task RunAsync_AllValidRecipients_Succeed(string recipient)
    {
        var result = await _vt.Vt.RunAsync("send_mail",
            $"{{\"to\": \"{recipient}\", \"from\": \"plan\", \"message\": \"test\"}}");

        Assert.True(result.IsSuccess,
            $"Expected success for recipient '{recipient}'. Output: {result.Output}");
    }

    [Fact]
    public async Task RunAsync_MissingTo_ReturnsFailure()
    {
        // --to is required; omitting it causes Spectre.Console.Cli to fail before ExecuteAsync
        var result = await _vt.Vt.RunAsync("send_mail",
            "{\"from\": \"plan\", \"message\": \"test\"}");

        Assert.False(result.IsSuccess);
    }
}
