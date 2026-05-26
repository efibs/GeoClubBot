using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UseCases.UseCases.SelfRoles;

namespace GeoClubBot.Discord.Services;

public class UpdateSelfRolesMessageService(
    DiscordBotReadyService botReadyService,
    IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botReadyService.DiscordSocketClientReady.ConfigureAwait(false);

        using var scope = serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

        await mediator.Send(new UpdateSelfRolesMessageCommand(), cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
