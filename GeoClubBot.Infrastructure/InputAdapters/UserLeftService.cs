using Configuration;
using Discord.WebSocket;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.DailyMissionReminder;

namespace Infrastructure.InputAdapters;

public partial class UserLeftService(
    DiscordSocketClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<DiscordConfiguration> config,
    ILogger<UserLeftService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserLeft += OnUserLeftAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserLeft -= OnUserLeftAsync;
        return Task.CompletedTask;
    }

    private async Task OnUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (guild.Id != config.Value.ServerId)
        {
            return;
        }

        var channel = guild.GetTextChannel(config.Value.LeftTextChannelId);
        var messageContent = config.Value.LeftMessage.Replace("{{User}}", user.Mention);
        await channel.SendMessageAsync(messageContent).ConfigureAwait(false);

        await DeactivateDailyMissionReminderAsync(user.Id).ConfigureAwait(false);
    }

    // The bot can no longer DM a user once they leave the server (Discord returns "no mutual guilds"),
    // so deactivate their daily mission reminder. UserLeft fires for voluntary leaves, kicks and bans
    // alike, covering every way a user can drop out of the guild.
    private async Task DeactivateDailyMissionReminderAsync(ulong discordUserId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            // A NotFound result simply means the user had no reminder configured — nothing to do.
            await mediator.Send(new StopDailyMissionReminderCommand(discordUserId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never let reminder cleanup break the goodbye message flow — log and move on.
            LogReminderDeactivationFailed(discordUserId, ex);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Failed to deactivate the daily mission reminder for user {DiscordUserId} after they left the server.")]
    partial void LogReminderDeactivationFailed(ulong discordUserId, Exception ex);
}
