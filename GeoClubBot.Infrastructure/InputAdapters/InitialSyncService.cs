using Microsoft.Extensions.Hosting;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters;

public class InitialSyncService(ISyncClubUseCase useCase) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await useCase.SyncClubAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}