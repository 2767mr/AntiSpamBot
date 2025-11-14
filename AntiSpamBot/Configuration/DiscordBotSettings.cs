namespace AntiSpamBot.Configuration;

public class DiscordBotSettings
{
    public const string SectionName = "DiscordBot";

    /// <summary>
    /// Discord bot token for authentication
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Channel ID where admin notifications will be sent
    /// </summary>
    public ulong AdminChannelId { get; set; }

    /// <summary>
    /// Duration in minutes to timeout spammers (default: 60 minutes)
    /// </summary>
    public int TimeoutDurationMinutes { get; set; } = 60;
}
