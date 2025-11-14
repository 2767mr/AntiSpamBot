using AntiSpamBot.Models;
using Discord;
using Discord.WebSocket;

namespace AntiSpamBot.Services;

public interface ISpamDetectionService
{
    /// <summary>
    /// Analyzes user messages to determine if they are a spammer
    /// </summary>
    /// <param name="userId">Discord user ID</param>
    /// <param name="guild">Discord guild</param>
    /// <returns>Spam detection result with analysis details</returns>
    Task<SpamDetectionResult> AnalyzeUserAsync(ulong userId, IGuild guild);
}
