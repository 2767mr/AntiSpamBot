using AntiSpamBot.Configuration;
using AntiSpamBot.Services;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AntiSpamBot.Tests.Services;

public class SpamDetectionServiceTests
{
    private readonly Mock<ILogger<SpamDetectionService>> _loggerMock;
    private readonly SpamDetectionSettings _settings;
    private readonly SpamDetectionService _service;

    public SpamDetectionServiceTests()
    {
        _loggerMock = new Mock<ILogger<SpamDetectionService>>();
        _settings = new SpamDetectionSettings
        {
            MessageHistoryDays = 7,
            MinimumOldestMessageMinutes = 10,
            MinimumChannelCount = 4
        };

        var optionsMock = new Mock<IOptions<SpamDetectionSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        _service = new SpamDetectionService(optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task AnalyzeUserAsync_NoMessages_ReturnsNotSpammer()
    {
        // Arrange
        var guildMock = CreateMockGuild(new List<(ulong channelId, List<MockMessage> messages)>());

        // Act
        var result = await _service.AnalyzeUserAsync(123456789, guildMock);

        // Assert
        Assert.False(result.IsSpammer);
        Assert.Equal(0, result.MessageCount);
        Assert.Contains("No messages found", result.Reason);
    }

    [Fact]
    public async Task AnalyzeUserAsync_OldestMessageTooOld_ReturnsNotSpammer()
    {
        // Arrange
        var userId = 123456789ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15), ChannelId = 1001 },
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-12), ChannelId = 1001 }
            }),
            (1002, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-11), ChannelId = 1002 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock);

        // Assert
        Assert.False(result.IsSpammer);
        Assert.Equal(3, result.MessageCount);
        Assert.True(result.OldestMessageAge?.TotalMinutes > 10);
        Assert.Contains("Oldest message is", result.Reason);
    }

    [Fact]
    public async Task AnalyzeUserAsync_FourChannelsWithinTimeframe_ReturnsSpammer()
    {
        // Arrange
        var userId = 123456789ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), ChannelId = 1001 }
            }),
            (1002, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4), ChannelId = 1002 }
            }),
            (1003, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3), ChannelId = 1003 }
            }),
            (1004, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2), ChannelId = 1004 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock);

        // Assert
        Assert.True(result.IsSpammer);
        Assert.Equal(4, result.MessageCount);
        Assert.Equal(4, result.UniqueChannelCount);
        Assert.Contains("4 channels", result.Reason);
    }

    [Fact]
    public async Task AnalyzeUserAsync_ThreeChannelsWithinTimeframe_ReturnsNotSpammer()
    {
        // Arrange
        var userId = 123456789ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), ChannelId = 1001 }
            }),
            (1002, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4), ChannelId = 1002 }
            }),
            (1003, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3), ChannelId = 1003 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock);

        // Assert
        Assert.False(result.IsSpammer);
        Assert.Equal(3, result.MessageCount);
        Assert.Equal(3, result.UniqueChannelCount);
        Assert.Contains("Only 3 channels", result.Reason);
    }

    [Fact]
    public async Task AnalyzeUserAsync_MultipleMessagesInSameChannel_CountsAsOneChannel()
    {
        // Arrange
        var userId = 123456789ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), ChannelId = 1001 },
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4), ChannelId = 1001 },
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3), ChannelId = 1001 },
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2), ChannelId = 1001 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock);

        // Assert
        Assert.False(result.IsSpammer);
        Assert.Equal(4, result.MessageCount);
        Assert.Equal(1, result.UniqueChannelCount);
    }

    [Fact]
    public async Task AnalyzeUserAsync_IgnoresOtherUsers_OnlyChecksTargetUser()
    {
        // Arrange
        var targetUserId = 123456789ul;
        var otherUserId = 987654321ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = targetUserId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), ChannelId = 1001 },
                new() { UserId = otherUserId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), ChannelId = 1001 }
            }),
            (1002, new List<MockMessage>
            {
                new() { UserId = otherUserId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4), ChannelId = 1002 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(targetUserId, guildMock);

        // Assert
        Assert.Equal(1, result.MessageCount);
        Assert.Equal(1, result.UniqueChannelCount);
        Assert.False(result.IsSpammer);
    }

    [Fact]
    public async Task AnalyzeUserAsync_EdgeCase_ExactlyTenMinutesOld_ReturnsSpammer()
    {
        // Arrange - exactly 10 minutes old should still be within the spam window
        var userId = 123456789ul;
        var messages = new List<(ulong channelId, List<MockMessage> messages)>
        {
            (1001, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10), ChannelId = 1001 }
            }),
            (1002, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-9), ChannelId = 1002 }
            }),
            (1003, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-8), ChannelId = 1003 }
            }),
            (1004, new List<MockMessage>
            {
                new() { UserId = userId, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-7), ChannelId = 1004 }
            })
        };

        var guildMock = CreateMockGuild(messages);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock);

        // Assert
        Assert.False(result.IsSpammer); // 10 minutes is the threshold, so NOT a spammer
        Assert.Equal(4, result.MessageCount);
        Assert.Equal(4, result.UniqueChannelCount);
    }

    [Fact]
    public async Task AnalyzeUserAsync_ChannelWithPermissionDenied_LogsDebugAndContinues()
    {
        // Arrange
        var userId = 123456789ul;
        var guildMock = new Mock<IGuild>();
        var textChannels = new List<ITextChannel>();

        // Channel 1: Accessible channel with messages
        var accessibleChannel = new Mock<ITextChannel>();
        accessibleChannel.Setup(x => x.Id).Returns(1001ul);
        var messages = new List<IMessage>
        {
            CreateMockMessage(userId, 1001ul, DateTimeOffset.UtcNow.AddMinutes(-5))
        };
        var asyncEnumerable = new TestAsyncEnumerable<IReadOnlyCollection<IMessage>>(
            new[] { messages as IReadOnlyCollection<IMessage> });
        accessibleChannel.Setup(x => x.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .Returns(asyncEnumerable);
        textChannels.Add(accessibleChannel.Object);

        // Channel 2: Restricted channel that throws permission exception
        var restrictedChannel = new Mock<ITextChannel>();
        restrictedChannel.Setup(x => x.Id).Returns(1002ul);
        // Create HttpException with MissingPermissions error code
        var httpException = new HttpException(System.Net.HttpStatusCode.Forbidden, null, DiscordErrorCode.MissingPermissions, null, null);
        restrictedChannel.Setup(x => x.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .Throws(httpException);
        textChannels.Add(restrictedChannel.Object);

        guildMock.Setup(x => x.GetTextChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(textChannels);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock.Object);

        // Assert
        Assert.Equal(1, result.MessageCount); // Only messages from accessible channel
        Assert.Equal(1, result.UniqueChannelCount);
        
        // Verify Debug log was called for permission denied (not Warning)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("does not have permission")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        // Verify Warning log was NOT called for this channel
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("1002")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task AnalyzeUserAsync_ChannelWithNonPermissionException_LogsWarning()
    {
        // Arrange
        var userId = 123456789ul;
        var guildMock = new Mock<IGuild>();
        var textChannels = new List<ITextChannel>();

        // Channel with a non-permission exception
        var faultyChannel = new Mock<ITextChannel>();
        faultyChannel.Setup(x => x.Id).Returns(1001ul);
        faultyChannel.Setup(x => x.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .Throws(new InvalidOperationException("Some other error"));
        textChannels.Add(faultyChannel.Object);

        guildMock.Setup(x => x.GetTextChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(textChannels);

        // Act
        var result = await _service.AnalyzeUserAsync(userId, guildMock.Object);

        // Assert
        Assert.Equal(0, result.MessageCount);
        
        // Verify Warning log was called for other exceptions
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to fetch messages")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private IMessage CreateMockMessage(ulong userId, ulong channelId, DateTimeOffset timestamp)
    {
        var msgMock = new Mock<IMessage>();
        msgMock.Setup(x => x.Id).Returns((ulong)Random.Shared.Next(1000000, 9999999));
        msgMock.Setup(x => x.Timestamp).Returns(timestamp);
        msgMock.Setup(x => x.Content).Returns("Test message");

        var authorMock = new Mock<IUser>();
        authorMock.Setup(x => x.Id).Returns(userId);
        msgMock.Setup(x => x.Author).Returns(authorMock.Object);

        var channelRefMock = new Mock<IMessageChannel>();
        channelRefMock.Setup(x => x.Id).Returns(channelId);
        msgMock.Setup(x => x.Channel).Returns(channelRefMock.Object);

        return msgMock.Object;
    }

    private IGuild CreateMockGuild(List<(ulong channelId, List<MockMessage> messages)> channelMessages)
    {
        var guildMock = new Mock<IGuild>();
        var textChannels = new List<ITextChannel>();

        foreach (var (channelId, messages) in channelMessages)
        {
            var channelMock = new Mock<ITextChannel>();
            channelMock.Setup(x => x.Id).Returns(channelId);

            // Create IMessage mocks from MockMessage
            var discordMessages = messages.Select(m =>
            {
                var msgMock = new Mock<IMessage>();
                msgMock.Setup(x => x.Id).Returns(m.MessageId);
                msgMock.Setup(x => x.Timestamp).Returns(m.Timestamp);
                msgMock.Setup(x => x.Content).Returns(m.Content);

                var authorMock = new Mock<IUser>();
                authorMock.Setup(x => x.Id).Returns(m.UserId);
                msgMock.Setup(x => x.Author).Returns(authorMock.Object);

                var channelRefMock = new Mock<IMessageChannel>();
                channelRefMock.Setup(x => x.Id).Returns(m.ChannelId);
                msgMock.Setup(x => x.Channel).Returns(channelRefMock.Object);

                return msgMock.Object;
            }).ToList();

            // Setup GetMessagesAsync to return our messages
            var asyncEnumerable = new TestAsyncEnumerable<IReadOnlyCollection<IMessage>>(
                new[] { discordMessages as IReadOnlyCollection<IMessage> });

            channelMock.Setup(x => x.GetMessagesAsync(It.IsAny<int>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(asyncEnumerable);

            textChannels.Add(channelMock.Object);
        }

        guildMock.Setup(x => x.GetTextChannelsAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(textChannels);

        return guildMock.Object;
    }

    public class MockMessage
    {
        public ulong MessageId { get; set; } = (ulong)Random.Shared.Next(1000000, 9999999);
        public ulong UserId { get; set; }
        public ulong ChannelId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Content { get; set; } = "Test message";
    }

    // Helper class to make async enumerable work with mocking
    private class TestAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _items;

        public TestAsyncEnumerable(IEnumerable<T> items)
        {
            _items = items;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new TestAsyncEnumerator<T>(_items.GetEnumerator());
        }
    }

    private class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _enumerator;

        public TestAsyncEnumerator(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

        public T Current => _enumerator.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(_enumerator.MoveNext());
        }

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return new ValueTask();
        }
    }
}
