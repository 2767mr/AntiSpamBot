using Discord;

namespace AntiSpamBot.Models;

public class UserMessage
{
    public ulong MessageId { get; set; }
    public ulong ChannelId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class SpamDetectionResult
{
    public bool IsSpammer { get; set; }
    public int MessageCount { get; set; }
    public int UniqueChannelCount { get; set; }
    public TimeSpan? OldestMessageAge { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<UserMessage> Messages { get; set; } = new();
}
