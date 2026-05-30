using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.SelfRoles;

public sealed record UpdateSelfRolesMessageCommand : ICommand;

public sealed class UpdateSelfRolesMessageHandler(
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IDiscordSelfUserAccess discordSelfUserAccess,
    IOptions<SelfRolesConfiguration> selfRolesOptions) : IRequestHandler<UpdateSelfRolesMessageCommand, Unit>
{
    private readonly ulong _textChannelId = selfRolesOptions.Value.TextChannelId;

    private readonly List<SelfRoleSetting> _selfRoleSettings = selfRolesOptions.Value.Roles;

    public async Task<Unit> Handle(UpdateSelfRolesMessageCommand request, CancellationToken cancellationToken)
    {
        var ownUserId = discordSelfUserAccess.GetSelfUserId();

        var oldMessageId = await discordTextChannelAccess
            .ReadLastMessageOfUserAsync(ownUserId, _textChannelId, 100, cancellationToken)
            .ConfigureAwait(false);

        var oldMessageExists = oldMessageId.HasValue;
        var selfRolesConfigured = _selfRoleSettings.Count > 0;

        switch (oldMessageExists, selfRolesConfigured)
        {
            case (false, true):
                await discordMessageAccess
                    .SendSelfRolesMessageAsync(_textChannelId, _selfRoleSettings, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case (true, true):
                await discordMessageAccess
                    .UpdateSelfRolesMessageAsync(_textChannelId, oldMessageId!.Value, _selfRoleSettings, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case (true, false):
                await discordMessageAccess
                    .DeleteMessageAsync(oldMessageId!.Value, _textChannelId, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }

        return Unit.Value;
    }
}
