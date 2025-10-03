using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UseCases.InputPorts.Organization;

namespace Infrastructure.InputAdapters;

public class InitialSyncService(DiscordBotReadyService botReadyService, IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for the bot to be ready
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);
        
        // Create a scope
        using var scope = serviceProvider.CreateScope();
        
        // Get the use case
        var useCase = scope.ServiceProvider.GetRequiredService<IInitialSyncClubUseCase>();
        
        await useCase.SyncClubAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}