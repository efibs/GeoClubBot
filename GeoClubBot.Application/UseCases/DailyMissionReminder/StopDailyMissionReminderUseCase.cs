using Microsoft.Extensions.Logging;
using UseCases.InputPorts.DailyMissionReminder;
using UseCases.OutputPorts;

namespace UseCases.UseCases.DailyMissionReminder;

public partial class StopDailyMissionReminderUseCase(IUnitOfWork unitOfWork, ILogger<StopDailyMissionReminderUseCase> logger)
    : IStopDailyMissionReminderUseCase
{
    public async Task<bool> StopReminderAsync(ulong discordUserId)
    {
        var existing = await unitOfWork.DailyMissionReminders.ReadReminderAsync(discordUserId).ConfigureAwait(false);

        if (existing == null)
        {
            LogNoReminderFound(discordUserId);
            return false;
        }

        unitOfWork.DailyMissionReminders.DeleteReminder(existing);
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        LogReminderStopped(discordUserId);
        return true;
    }

    [LoggerMessage(LogLevel.Debug, "No daily mission reminder found for user {DiscordUserId}.")]
    partial void LogNoReminderFound(ulong discordUserId);

    [LoggerMessage(LogLevel.Information, "Daily mission reminder stopped for user {DiscordUserId}.")]
    partial void LogReminderStopped(ulong discordUserId);
}
