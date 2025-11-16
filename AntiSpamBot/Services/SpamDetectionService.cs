using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public class SpamDetectionService : ISpamDetectionService
{
    private readonly SpamDetectionSettings _settings;
    private readonly ILogger<SpamDetectionService> _logger;

    public SpamDetectionService(
        IOptions<SpamDetectionSettings> settings,
        ILogger<SpamDetectionService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SpamDetectionResult> AnalyzeUserAsync(ulong userId, IGuild guild)
    {
        var result = new SpamDetectionResult();
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_settings.MessageHistoryDays);

        _logger.LogInformation("Analyzing user {UserId} for spam. Looking back {Days} days", userId, _settings.MessageHistoryDays);

        // Get all text channels from the guild
        var channels = await guild.GetTextChannelsAsync();
        
        // Collect all messages from the user across all accessible channels
        var userMessages = new List<UserMessage>();

        foreach (var channel in channels)
        {
            try
            {
                // Get messages from this channel
                var messages = await channel.GetMessagesAsync(limit: 100).FlattenAsync();
                
                var relevantMessages = messages
                    .Where(m => m.Author.Id == userId && m.Timestamp >= cutoffDate)
                    .Select(m => new UserMessage
                    {
                        MessageId = m.Id,
                        ChannelId = m.Channel.Id,
                        Timestamp = m.Timestamp,
                        Content = m.Content
                    });

                userMessages.AddRange(relevantMessages);
            }
            catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Bot doesn't have permission to read this channel - log at debug level to avoid spam
                _logger.LogDebug("Bot does not have permission to read messages from channel {ChannelId}", channel.Id);
            }
            catch (Exception ex)
            {
                // Other exceptions should still be logged as warnings
                _logger.LogWarning(ex, "Failed to fetch messages from channel {ChannelId}", channel.Id);
            }
        }

        result.Messages = userMessages.OrderBy(m => m.Timestamp).ToList();
        result.MessageCount = userMessages.Count;

        if (!userMessages.Any())
        {
            result.IsSpammer = false;
            result.Reason = "No messages found in the last 7 days";
            _logger.LogInformation("User {UserId} has no messages in the lookback period", userId);
            return result;
        }

        // Get unique channel count
        var uniqueChannels = userMessages.Select(m => m.ChannelId).Distinct().ToList();
        result.UniqueChannelCount = uniqueChannels.Count;

        // Get oldest message age
        var oldestMessage = userMessages.OrderBy(m => m.Timestamp).First();
        var oldestMessageAge = DateTimeOffset.UtcNow - oldestMessage.Timestamp;
        result.OldestMessageAge = oldestMessageAge;

        _logger.LogInformation(
            "User {UserId} analysis: {MessageCount} messages, {ChannelCount} channels, oldest message {Age} minutes old",
            userId, result.MessageCount, result.UniqueChannelCount, oldestMessageAge.TotalMinutes);

        // Apply spam detection logic
        // If the oldest message is older than 10 minutes, it is not a spammer
        if (oldestMessageAge.TotalMinutes > _settings.MinimumOldestMessageMinutes)
        {
            result.IsSpammer = false;
            result.Reason = $"Oldest message is {oldestMessageAge.TotalMinutes:F1} minutes old (threshold: {_settings.MinimumOldestMessageMinutes} minutes)";
            _logger.LogInformation("User {UserId} is not a spammer - oldest message too old", userId);
            return result;
        }

        // If messages are in at least 4 different channels, it is a spammer
        if (result.UniqueChannelCount >= _settings.MinimumChannelCount)
        {
            result.IsSpammer = true;
            result.Reason = $"Messages in {result.UniqueChannelCount} channels within {oldestMessageAge.TotalMinutes:F1} minutes (threshold: {_settings.MinimumChannelCount} channels)";
            _logger.LogWarning("User {UserId} detected as SPAMMER - {ChannelCount} channels", userId, result.UniqueChannelCount);
            return result;
        }

        result.IsSpammer = false;
        result.Reason = $"Only {result.UniqueChannelCount} channels (threshold: {_settings.MinimumChannelCount})";
        _logger.LogInformation("User {UserId} is not a spammer - insufficient channel count", userId);
        
        return result;
    }
}
