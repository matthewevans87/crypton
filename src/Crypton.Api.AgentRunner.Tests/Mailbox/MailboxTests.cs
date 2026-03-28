using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Mailbox;

/// <summary>Additional mailbox tests (FileMailboxService).</summary>
public class AdditionalMailboxTests : IDisposable
{
    private readonly string _tempPath;
    private readonly FileMailboxService _service;

    public AdditionalMailboxTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"mailbox2_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);
        _service = new FileMailboxService(new StorageConfig
        {
            BasePath = _tempPath,
            MailboxesPath = Path.Combine(_tempPath, "mailboxes"),
            MaxMailboxMessages = 5
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void Send_MessageFields_ArePreserved()
    {
        var ts = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        _service.Send(new MailboxMessage("plan", "research", "Test content", ts));
        var msgs = _service.GetMessages("research", 1);
        Assert.Single(msgs);
        Assert.Equal("plan", msgs[0].FromAgent);
        Assert.Equal("research", msgs[0].ToAgent);
        Assert.Equal("Test content", msgs[0].Content);
    }

    [Fact]
    public void Send_MultipleMessages_MaintainsOrder()
    {
        for (var i = 0; i < 3; i++)
            _service.Send(new MailboxMessage("plan", "research", $"msg {i}", DateTimeOffset.UtcNow));

        var msgs = _service.GetMessages("research", 10);
        Assert.Equal(3, msgs.Count);
    }
}
