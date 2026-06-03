using AntiSpamBot.Configuration;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace AntiSpamBot.Services;

public class DiscordBotService : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordBotSettings _settings;
    private readonly ISpamDetectionService _spamDetectionService;
    private readonly IActionReporterService _actionReporter;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DiscordBotService(
        IOptions<DiscordBotSettings> settings,
        ISpamDetectionService spamDetectionService,
        IActionReporterService actionReporter,
        ILogger<DiscordBotService> logger,
        IServiceProvider serviceProvider)
    {
        _settings = settings.Value;
        _spamDetectionService = spamDetectionService;
        _actionReporter = actionReporter;
        _logger = logger;
        _serviceProvider = serviceProvider;

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | 
                           GatewayIntents.GuildMessages |
                           GatewayIntents.GuildMembers |
                           GatewayIntents.MessageContent |
                           GatewayIntents.GuildBans,
            AlwaysDownloadUsers = true
        };

        _client = new DiscordSocketClient(config);
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;
    }

    public bool IsReady => _client.ConnectionState == ConnectionState.Connected;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.Token))
        {
            _logger.LogError("Discord bot token is not configured");
            throw new InvalidOperationException("Discord bot token is required");
        }

        _logger.LogInformation("Starting Discord bot service");
        
        await _client.LoginAsync(TokenType.Bot, _settings.Token);
        await _client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord bot service");
        
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private Task LogAsync(LogMessage log)
    {
        var logLevel = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, log.Exception, "{Source}: {Message}", log.Source, log.Message);
        return Task.CompletedTask;
    }

    private Task ReadyAsync()
    {
        _logger.LogInformation("Discord bot is connected and ready. Logged in as {Username}#{Discriminator}", 
            _client.CurrentUser.Username, _client.CurrentUser.Discriminator);
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // Ignore bot messages
        if (message.Author.IsBot)
            return;

        // Only handle guild messages
        if (message.Channel is not SocketTextChannel textChannel)
            return;

        try
        {
            var guild = textChannel.Guild;
            var user = message.Author as SocketGuildUser;

            if (user == null)
                return;

            _logger.LogDebug("Processing message from {Username} ({UserId}) in channel {ChannelName}", 
                user.Username, user.Id, textChannel.Name);

            // Analyze the user for spam
            var result = await _spamDetectionService.AnalyzeUserAsync(user.Id, guild);

            if (result.IsSpammer)
            {
                _logger.LogWarning("SPAMMER DETECTED: {Username} ({UserId}) - {Reason}", 
                    user.Username, user.Id, result.Reason);

                // Timeout the user
                var timeoutDuration = TimeSpan.FromMinutes(_settings.TimeoutDurationMinutes);
                await user.SetTimeOutAsync(timeoutDuration);
                _logger.LogInformation("User {Username} timed out for {Duration} minutes", 
                    user.Username, _settings.TimeoutDurationMinutes);

                // Delete all recent messages found during spam analysis
                var deletedCount = 0;
                foreach (var msg in result.Messages)
                {
                    try
                    {
                        _logger.LogDebug("Attempting to delete message {MessageId} from channel {ChannelId}", msg.MessageId, msg.ChannelId);
                        
                        var channel = guild.GetTextChannel(msg.ChannelId);
                        if (channel == null)
                        {
                            _logger.LogWarning("Channel {ChannelId} not found in guild cache for message deletion", msg.ChannelId);
                            continue;
                        }

                        _logger.LogDebug("Channel {ChannelName} ({ChannelId}) found, fetching message", channel.Name, channel.Id);
                        
                        var msgToDelete = await channel.GetMessageAsync(msg.MessageId);
                        if (msgToDelete == null)
                        {
                            _logger.LogWarning("Message {MessageId} not found in channel {ChannelName} ({ChannelId})", 
                                msg.MessageId, channel.Name, channel.Id);
                            continue;
                        }

                        _logger.LogDebug("Message {MessageId} found, attempting to delete", msg.MessageId);
                        await msgToDelete.DeleteAsync();
                        deletedCount++;
                        _logger.LogDebug("Successfully deleted message {MessageId}", msg.MessageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete message {MessageId} from channel {ChannelId}", 
                            msg.MessageId, msg.ChannelId);
                    }
                }

                _logger.LogInformation("Deleted {Count} messages from spammer {Username}", 
                    deletedCount, user.Username);

                // Report to admins
                await _actionReporter.ReportSpammerActionAsync(
                    _client,
                    user,
                    result,
                    deletedCount,
                    timeoutDuration);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Author}", message.Author.Username);
        }
    }
}
