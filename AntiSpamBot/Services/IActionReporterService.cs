using AntiSpamBot.Models;
using Discord;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface IActionReporterService
{
    /// <summary>
    /// Reports spam detection and actions taken to administrators
    /// </summary>
    Task ReportSpammerActionAsync(
        IDiscordClient client,
        IGuildUser user,
        SpamDetectionResult detectionResult,
        int deletedMessageCount,
        TimeSpan timeoutDuration);
}
