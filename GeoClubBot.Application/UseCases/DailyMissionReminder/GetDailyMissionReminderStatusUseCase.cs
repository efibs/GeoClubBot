using UseCases.InputPorts.DailyMissionReminder;
using UseCases.OutputPorts;

namespace UseCases.UseCases.DailyMissionReminder;

public class GetDailyMissionReminderStatusUseCase(IUnitOfWork unitOfWork) : IGetDailyMissionReminderStatusUseCase
{
    public async Task<Entities.DailyMissionReminder?> GetStatusAsync(ulong discordUserId)
    {
        return await unitOfWork.DailyMissionReminders.ReadReminderAsync(discordUserId).ConfigureAwait(false);
    }
}
