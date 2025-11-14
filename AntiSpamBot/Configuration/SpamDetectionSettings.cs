namespace AntiSpamBot.Configuration;

public class SpamDetectionSettings
{
    public const string SectionName = "SpamDetection";

    /// <summary>
    /// Number of days to look back for user messages (default: 7 days)
    /// </summary>
    public int MessageHistoryDays { get; set; } = 7;

    /// <summary>
    /// Minimum age in minutes for oldest message to not be spam (default: 10 minutes)
    /// </summary>
    public int MinimumOldestMessageMinutes { get; set; } = 10;

    /// <summary>
    /// Minimum number of different channels to be considered spam (default: 4)
    /// </summary>
    public int MinimumChannelCount { get; set; } = 4;
}
