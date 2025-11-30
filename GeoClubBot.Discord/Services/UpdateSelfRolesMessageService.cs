using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UseCases.InputPorts.SelfRoles;

namespace GeoClubBot.Discord.Services;

public class UpdateSelfRolesMessageService(DiscordBotReadyService botReadyService, IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for the bot to be ready
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);
        
        // Create a scope
        using var scope = serviceProvider.CreateScope();
        
        // Get the use case
        var useCase = scope.ServiceProvider.GetRequiredService<IUpdateSelfRolesMessageUseCase>();
        
        await useCase.UpdateSelfRolesMessageAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do
        return Task.CompletedTask;
    }
}