using Configuration;
using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Club;
using UseCases.UseCases.MemberPrivateChannels;

namespace Infrastructure.InputAdapters;

public partial class InitialSyncService(
    DiscordBotReadyService botReadyService,
    IServiceScopeFactory scopeFactory,
    IOptions<MemberPrivateChannelsConfiguration> privateChannelOptions,
    ILogger<InitialSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            await mediator.Send(new SyncClubsCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogInitialSyncFailed(logger, ex);
        }

        if (privateChannelOptions.Value.ReconcileOnStartup)
        {
            await ReconcilePrivateChannelsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs after the sync, so membership is current: moves every private channel into its club's
    /// category, recreates any whose creation failed and fixes permissions. Its own scope, so the sync's
    /// tracked entities don't leak into it.
    /// </summary>
    private async Task ReconcilePrivateChannelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await mediator
                .Send(new ReconcileMemberPrivateChannelsCommand(DryRun: false), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogReconcileRejected(logger, result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            LogReconcileFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Error, "Error during initial sync of clubs.")]
    static partial void LogInitialSyncFailed(ILogger<InitialSyncService> logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Reconciling the member private channels at start-up failed: {Error}")]
    static partial void LogReconcileRejected(ILogger<InitialSyncService> logger, string error);

    [LoggerMessage(LogLevel.Error, "Error while reconciling the member private channels at start-up.")]
    static partial void LogReconcileFailed(ILogger<InitialSyncService> logger, Exception ex);
}
