using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using AntiSpamBot.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AntiSpamBot.Tests.Services;

public class ActionReporterServiceTests
{
    private readonly Mock<ILogger<ActionReporterService>> _loggerMock;
    private readonly DiscordBotSettings _settings;
    private readonly ActionReporterService _service;

    public ActionReporterServiceTests()
    {
        _loggerMock = new Mock<ILogger<ActionReporterService>>();
        _settings = new DiscordBotSettings
        {
            AdminChannelId = 999999999,
            TimeoutDurationMinutes = 60
        };

        var optionsMock = new Mock<IOptions<DiscordBotSettings>>();
        optionsMock.Setup(x => x.Value).Returns(_settings);

        _service = new ActionReporterService(optionsMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ReportSpammerActionAsync_NoAdminChannelConfigured_LogsWarning()
    {
        // Arrange
        _settings.AdminChannelId = 0;
        var clientMock = new Mock<IDiscordClient>();
        var user = CreateMockUser("TestUser", "0000", 123456789);
        var result = new SpamDetectionResult { IsSpammer = true };

        // Act
        await _service.ReportSpammerActionAsync(
            clientMock.Object,
            user,
            result,
            5,
            TimeSpan.FromMinutes(60));

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Admin channel ID not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportSpammerActionAsync_ChannelNotFound_LogsError()
    {
        // Arrange
        var clientMock = new Mock<IDiscordClient>();
        clientMock.Setup(x => x.GetChannelAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync((IChannel?)null);

        var user = CreateMockUser("TestUser", "1234", 123456789);

        var result = new SpamDetectionResult
        {
            IsSpammer = true,
            MessageCount = 10,
            UniqueChannelCount = 5,
            Reason = "Test reason"
        };

        // Act
        await _service.ReportSpammerActionAsync(
            clientMock.Object,
            user,
            result,
            10,
            TimeSpan.FromMinutes(60));

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Could not find admin channel")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ReportSpammerActionAsync_ValidChannel_SendsEmbedMessage()
    {
        // Arrange
        var channelMock = new Mock<IMessageChannel>();
        Embed? capturedEmbed = null;

        channelMock.Setup(x => x.SendMessageAsync(
                null,
                false,
                It.IsAny<Embed>(),
                null,
                null,
                null,
                null,
                null,
                null,
                MessageFlags.None,
                null))
            .Callback<string?, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[], Embed[], MessageFlags, PollProperties?>(
                (text, tts, embed, opts, mentions, reference, components, stickers, embeds, flags, poll) =>
                {
                    capturedEmbed = embed;
                })
            .ReturnsAsync((IUserMessage)null!);

        var clientMock = new Mock<IDiscordClient>();
        clientMock.Setup(x => x.GetChannelAsync(_settings.AdminChannelId, It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(channelMock.Object as IChannel);

        var user = CreateMockUser("SpammerUser", "9999", 123456789);

        var result = new SpamDetectionResult
        {
            IsSpammer = true,
            MessageCount = 15,
            UniqueChannelCount = 5,
            OldestMessageAge = TimeSpan.FromMinutes(8),
            Reason = "Messages in 5 channels within 8 minutes",
            Messages = new List<UserMessage>
            {
                new() { ChannelId = 1001 },
                new() { ChannelId = 1002 },
                new() { ChannelId = 1003 }
            }
        };

        // Act
        await _service.ReportSpammerActionAsync(
            clientMock.Object,
            user,
            result,
            15,
            TimeSpan.FromMinutes(60));

        // Assert
        channelMock.Verify(x => x.SendMessageAsync(
            null,
            false,
            It.IsAny<Embed>(),
            null,
            null,
            null,
            null,
            null,
            null,
            MessageFlags.None,
            null), Times.Once);

        Assert.NotNull(capturedEmbed);
        Assert.Contains("Spammer Detected", capturedEmbed.Title);
    }

    [Fact]
    public async Task ReportSpammerActionAsync_IncludesAllRelevantInformation()
    {
        // Arrange
        var channelMock = new Mock<IMessageChannel>();
        Embed? capturedEmbed = null;

        channelMock.Setup(x => x.SendMessageAsync(
                null,
                false,
                It.IsAny<Embed>(),
                null,
                null,
                null,
                null,
                null,
                null,
                MessageFlags.None,
                null))
            .Callback<string?, bool, Embed?, RequestOptions?, AllowedMentions?, MessageReference?, MessageComponent?, ISticker[], Embed[], MessageFlags, PollProperties?>(
                (text, tts, embed, opts, mentions, reference, components, stickers, embeds, flags, poll) =>
                {
                    capturedEmbed = embed;
                })
            .ReturnsAsync((IUserMessage)null!);

        var clientMock = new Mock<IDiscordClient>();
        clientMock.Setup(x => x.GetChannelAsync(_settings.AdminChannelId, It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
            .ReturnsAsync(channelMock.Object as IChannel);

        var user = CreateMockUser("TestSpammer", "1234", 987654321);

        var result = new SpamDetectionResult
        {
            IsSpammer = true,
            MessageCount = 20,
            UniqueChannelCount = 6,
            OldestMessageAge = TimeSpan.FromMinutes(7.5),
            Reason = "Test spam reason"
        };

        // Act
        await _service.ReportSpammerActionAsync(
            clientMock.Object,
            user,
            result,
            18,
            TimeSpan.FromMinutes(60));

        // Assert
        Assert.NotNull(capturedEmbed);
        Assert.Equal(Color.Red.RawValue, capturedEmbed.Color?.RawValue);
        
        var fields = capturedEmbed.Fields;
        Assert.Contains(fields, f => f.Name == "User ID" && f.Value == "987654321");
        Assert.Contains(fields, f => f.Name == "Detection Reason" && f.Value == "Test spam reason");
        Assert.Contains(fields, f => f.Name == "Messages Found" && f.Value == "20");
        Assert.Contains(fields, f => f.Name == "Unique Channels" && f.Value == "6");
        Assert.Contains(fields, f => f.Name == "Actions Taken" && f.Value.Contains("60 minutes"));
        Assert.Contains(fields, f => f.Name == "Actions Taken" && f.Value.Contains("18 messages"));
    }

    /// <summary>
    /// Helper method to create a mock IGuildUser with the required properties
    /// </summary>
    private IGuildUser CreateMockUser(string username, string discriminator, ulong id)
    {
        var userMock = new Mock<IGuildUser>();
        
        userMock.Setup(x => x.Username).Returns(username);
        userMock.Setup(x => x.Discriminator).Returns(discriminator);
        userMock.Setup(x => x.Id).Returns(id);
        userMock.Setup(x => x.Mention).Returns($"<@{id}>");
        
        return userMock.Object;
    }
}
