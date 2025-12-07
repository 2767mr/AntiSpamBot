using AntiSpamBot.Configuration;
using AntiSpamBot.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public class ActionReporterService : IActionReporterService
{
    private readonly DiscordBotSettings _settings;
    private readonly ILogger<ActionReporterService> _logger;

    public ActionReporterService(
        IOptions<DiscordBotSettings> settings,
        ILogger<ActionReporterService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task ReportSpammerActionAsync(
        IDiscordClient client,
        IGuildUser user,
        SpamDetectionResult detectionResult,
        int deletedMessageCount,
        TimeSpan timeoutDuration)
    {
        if (_settings.AdminChannelId == 0)
        {
            _logger.LogWarning("Admin channel ID not configured, skipping report");
            return;
        }

        try
        {
            var adminChannel = await client.GetChannelAsync(_settings.AdminChannelId) as IMessageChannel;
            
            if (adminChannel == null)
            {
                _logger.LogError("Could not find admin channel with ID {ChannelId}", _settings.AdminChannelId);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Spammer Detected and Actioned")
                .WithColor(Color.Red)
                .WithCurrentTimestamp()
                .AddField("User", $"{user.Username}#{user.Discriminator} ({user.Mention})", inline: true)
                .AddField("User ID", user.Id.ToString(), inline: true)
                .AddField("Detection Reason", detectionResult.Reason, inline: false)
                .AddField("Messages Found", detectionResult.MessageCount.ToString(), inline: true)
                .AddField("Unique Channels", detectionResult.UniqueChannelCount.ToString(), inline: true)
                .AddField("Oldest Message Age", 
                    detectionResult.OldestMessageAge.HasValue 
                        ? $"{detectionResult.OldestMessageAge.Value.TotalMinutes:F1} minutes" 
                        : "N/A", 
                    inline: true)
                .AddField("Actions Taken", 
                    $"* Timed out for {timeoutDuration.TotalMinutes} minutes\n" +
                    $"* Deleted {deletedMessageCount} messages",
                    inline: false);

            // Add a sample of channels where messages were found
            if (detectionResult.Messages.Any())
            {
                var channelSample = detectionResult.Messages
                    .GroupBy(m => m.ChannelId)
                    .Select(g => $"<#{g.Key}> ({g.Count()} messages)")
                    .Take(10);

                embed.AddField("Affected Channels", string.Join("\n", channelSample), inline: false);
            }

            await adminChannel.SendMessageAsync(embed: embed.Build());
            
            _logger.LogInformation("Spam report sent to admin channel for user {Username}", user.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send report to admin channel");
        }
    }
}
