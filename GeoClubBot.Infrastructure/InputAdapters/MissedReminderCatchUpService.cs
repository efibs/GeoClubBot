using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.DailyMissionReminder;

namespace Infrastructure.InputAdapters;

/// <summary>
/// Sends daily mission reminders that were missed while the bot was down, once at startup. Waits
/// for the Discord gateway to be ready (DMs need a logged-in client). Because hosted services
/// start sequentially and Quartz is registered after this one, the catch-up completes before the
/// per-minute reminder job can fire, so the two can never race on the same reminder.
/// </summary>
public partial class MissedReminderCatchUpService(
    DiscordBotReadyService botReadyService,
    IServiceScopeFactory scopeFactory,
    ILogger<MissedReminderCatchUpService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            await mediator.Send(new CatchUpMissedRemindersCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCatchUpFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Error, "Error while catching up missed daily mission reminders.")]
    static partial void LogCatchUpFailed(ILogger<MissedReminderCatchUpService> logger, Exception ex);
}
