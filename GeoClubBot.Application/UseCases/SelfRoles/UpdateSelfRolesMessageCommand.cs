using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.SelfRoles;

public sealed record UpdateSelfRolesMessageCommand : ICommand;

public sealed class UpdateSelfRolesMessageHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IDiscordSelfUserAccess discordSelfUserAccess,
    IConfiguration config) : IRequestHandler<UpdateSelfRolesMessageCommand, Unit>
{
    private readonly ulong _textChannelId =
        config.GetValue<ulong>(ConfigKeys.SelfRolesTextChannelIdConfigurationKey);

    private readonly List<SelfRoleSetting> _selfRoleSettings = config
        .GetSection(ConfigKeys.SelfRolesRolesConfigurationKey)
        .Get<List<SelfRoleSetting>>()!;

    public async Task<Unit> Handle(UpdateSelfRolesMessageCommand request, CancellationToken cancellationToken)
    {
        var ownUserId = discordSelfUserAccess.GetSelfUserId();

        var oldMessageId = await discordTextChannelAccess
            .ReadLastMessageOfUserAsync(ownUserId, _textChannelId, 100)
            .ConfigureAwait(false);

        var oldMessageExists = oldMessageId.HasValue;
        var selfRolesConfigured = _selfRoleSettings.Count > 0;

        switch (oldMessageExists, selfRolesConfigured)
        {
            case (false, true):
                await discordMessageAccess
                    .SendSelfRolesMessageAsync(_textChannelId, _selfRoleSettings)
                    .ConfigureAwait(false);
                break;
            case (true, true):
                await discordMessageAccess
                    .UpdateSelfRolesMessageAsync(_textChannelId, oldMessageId!.Value, _selfRoleSettings)
                    .ConfigureAwait(false);
                break;
            case (true, false):
                await discordMessageAccess
                    .DeleteMessageAsync(oldMessageId!.Value, _textChannelId)
                    .ConfigureAwait(false);
                break;
        }

        return Unit.Value;
    }
}
