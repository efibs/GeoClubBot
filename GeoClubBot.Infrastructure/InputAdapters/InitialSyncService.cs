using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters;

public class InitialSyncService(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Create a scope
        using var scope = serviceProvider.CreateScope();
        
        // Get the use case
        var useCase = scope.ServiceProvider.GetRequiredService<ISyncClubUseCase>();
        
        await useCase.SyncClubAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}