using GeoClubBot.Discord.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.Club;

namespace Infrastructure.InputAdapters;

public partial class InitialSyncService(
    DiscordBotReadyService botReadyService,
    IServiceScopeFactory scopeFactory,
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Error, "Error during initial sync of clubs.")]
    static partial void LogInitialSyncFailed(ILogger<InitialSyncService> logger, Exception ex);
}
