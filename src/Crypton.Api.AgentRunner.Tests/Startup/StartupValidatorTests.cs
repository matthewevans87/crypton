using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Startup;

public class FileMailboxServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly FileMailboxService _service;

    public FileMailboxServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"mailbox_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);
        _service = new FileMailboxService(new StorageConfig
        {
            BasePath = _tempPath,
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MaxMailboxMessages = 3
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void Send_StoresMessage()
    {
        var msg = new MailboxMessage("plan", "research", "Hello", DateTimeOffset.UtcNow);
        _service.Send(msg);
        var messages = _service.GetMessages("research", 10);
        Assert.Single(messages);
        Assert.Equal("Hello", messages[0].Content);
    }

    [Fact]
    public void GetMessages_EmptyMailbox_ReturnsEmpty()
    {
        Assert.Empty(_service.GetMessages("research", 10));
    }

    [Fact]
    public void Send_EnforcesMaxMessagesLimit()
    {
        for (var i = 0; i < 5; i++)
            _service.Send(new MailboxMessage("plan", "research", $"msg {i}", DateTimeOffset.UtcNow));
        Assert.Equal(3, _service.GetMessages("research", 10).Count);
    }

    [Fact]
    public void Send_MultipleAgents_RoutesCorrectly()
    {
        _service.Send(new MailboxMessage("plan", "research", "for research", DateTimeOffset.UtcNow));
        _service.Send(new MailboxMessage("plan", "analysis", "for analysis", DateTimeOffset.UtcNow));
        Assert.Single(_service.GetMessages("research", 10));
        Assert.Single(_service.GetMessages("analysis", 10));
        Assert.Empty(_service.GetMessages("plan", 10));
    }
}
