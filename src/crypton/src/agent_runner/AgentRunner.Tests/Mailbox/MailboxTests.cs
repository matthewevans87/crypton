using AgentRunner.Mailbox;

namespace AgentRunner.Tests.Mailbox;

public class MailboxTests : IDisposable
{
    private readonly string _testPath;
    private readonly Mailbox _mailbox;

    public MailboxTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"mailbox_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testPath);
        _mailbox = new Mailbox("test", _testPath, 5);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPath))
        {
            Directory.Delete(_testPath, true);
        }
    }

    [Fact]
    public void Deposit_MessageAdded()
    {
        var message = new MailboxMessage
        {
            FromAgent = "plan",
            ToAgent = "research",
            Content = "Test message",
            Type = MessageType.Forward
        };

        _mailbox.Deposit(message);

        var messages = _mailbox.GetMessages(5);
        Assert.Single(messages);
        Assert.Equal("Test message", messages[0].Content);
    }

    [Fact]
    public void GetMessages_RespectsCountLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            _mailbox.Deposit(new MailboxMessage
            {
                FromAgent = "plan",
                Content = $"Message {i}"
            });
        }

        var messages = _mailbox.GetMessages(5);
        Assert.Equal(5, messages.Count);
    }

    [Fact]
    public void Deposit_OldMessagesPruned()
    {
        for (int i = 0; i < 10; i++)
        {
            _mailbox.Deposit(new MailboxMessage
            {
                FromAgent = "plan",
                Content = $"Message {i}"
            });
        }

        var messages = _mailbox.GetMessages(10);
        Assert.Equal(5, messages.Count);
    }

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        _mailbox.Deposit(new MailboxMessage { FromAgent = "plan", Content = "Test" });
        _mailbox.Clear();

        var messages = _mailbox.GetMessages(5);
        Assert.Empty(messages);
    }

    [Fact]
    public void MailboxMessage_ToFileLine_Parsable()
    {
        var original = new MailboxMessage
        {
            Id = "test-id",
            FromAgent = "plan",
            ToAgent = "research",
            Content = "Test content",
            Type = MessageType.Forward
        };

        var line = original.ToFileLine();
        var parsed = MailboxMessage.Parse(line);

        Assert.Equal(original.Id, parsed.Id);
        Assert.Equal(original.FromAgent, parsed.FromAgent);
        Assert.Equal(original.ToAgent, parsed.ToAgent);
        Assert.Equal(original.Content, parsed.Content);
        Assert.Equal(original.Type, parsed.Type);
    }
}
