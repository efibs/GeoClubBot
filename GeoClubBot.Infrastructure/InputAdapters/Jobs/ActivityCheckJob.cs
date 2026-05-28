using Configuration;
using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using QuartzExtensions;
using UseCases.UseCases.ClubMemberActivity;
using UseCases.UseCases.Organization;

namespace Infrastructure.InputAdapters.Jobs;

[DisallowConcurrentExecution]
[ConfiguredCronJob(ConfigKeys.ActivityCheckerCronScheduleConfigurationKey)]
public class ActivityCheckJob(
    ISender mediator,
    IServiceScopeFactory scopeFactory,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<ActivityCheckJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        // Each club gets its own DI scope so the parallel branches don't share a DbContext.
        var perClubResults = await Task.WhenAll(geoGuessrConfig.Value.Clubs.Select(async club =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scopedMediator = scope.ServiceProvider.GetRequiredService<ISender>();
            try
            {
                return await scopedMediator
                    .Send(new CheckGeoGuessrPlayerActivityCommand(club.ClubId), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking player activity for club {ClubId}.", club.ClubId);
                return new List<ClubMemberActivityStatus>();
            }
        })).ConfigureAwait(false);

        var newStatuses = perClubResults.SelectMany(s => s).ToList();

        try
        {
            await mediator
                .Send(new ClubMemberActivityRewardCommand(newStatuses), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rewarding member activity.");
        }

        // Cleanup runs last so deletions don't strand members without history entries.
        try
        {
            await mediator.Send(new CleanupCommand(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running cleanup.");
        }
    }
}
