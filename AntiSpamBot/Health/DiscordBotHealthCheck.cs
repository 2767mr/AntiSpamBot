using AntiSpamBot.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AntiSpamBot.Health;

public class DiscordBotHealthCheck : IHealthCheck
{
    private readonly DiscordBotService _botService;
    private readonly ILogger<DiscordBotHealthCheck> _logger;

    public DiscordBotHealthCheck(
        DiscordBotService botService,
        ILogger<DiscordBotHealthCheck> logger)
    {
        _botService = botService;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_botService.IsReady)
            {
                return Task.FromResult(
                    HealthCheckResult.Healthy("Discord bot is connected and ready"));
            }

            _logger.LogWarning("Discord bot is not ready");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Discord bot is not connected"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Health check failed", ex));
        }
    }
}
