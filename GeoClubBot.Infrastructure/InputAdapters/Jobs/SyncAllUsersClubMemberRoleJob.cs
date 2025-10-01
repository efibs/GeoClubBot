using Constants;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzExtensions;
using UseCases.InputPorts.Club;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.GeoGuessrAccountLinkingSyncAllUsersRolesScheduleConfigurationKey)]
public class SyncAllUsersClubMemberRoleJob(ISyncClubMemberRoleUseCase useCase, ILogger<SyncAllUsersClubMemberRoleJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await useCase.SyncAllUsersClubMemberRoleAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error syncing all users club member role.");
        }
    }
}