using AntiSpamBot.Configuration;
using AntiSpamBot.Health;
using AntiSpamBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Services.Configure<DiscordBotSettings>(
    builder.Configuration.GetSection(DiscordBotSettings.SectionName));
builder.Services.Configure<SpamDetectionSettings>(
    builder.Configuration.GetSection(SpamDetectionSettings.SectionName));

// Add services
builder.Services.AddSingleton<ISpamDetectionService, SpamDetectionService>();
builder.Services.AddSingleton<IActionReporterService, ActionReporterService>();
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DiscordBotService>());

builder.Services.AddOpenApi();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<DiscordBotHealthCheck>("discord_bot", tags: new[] { "ready" });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Configure health check endpoints for Kubernetes
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // No health checks, just returns healthy if app is running
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapGet("/", () => Results.Ok(new
{
    service = "AntiSpamBot",
    status = "running",
    version = "1.0.0"
}));

app.Run();
