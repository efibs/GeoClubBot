using Constants;
using Entities;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

public sealed record CreateMemberPrivateChannelCommand(ClubMember ClubMember) : ICommand<ulong?>;

public sealed partial class CreateMemberPrivateChannelHandler(
    IClubMemberRepository clubMembers,
    IDiscordTextChannelAccess discordTextChannelAccess,
    IDiscordMessageAccess discordMessageAccess,
    IConfiguration config,
    ILogger<CreateMemberPrivateChannelHandler> logger)
    : IRequestHandler<CreateMemberPrivateChannelCommand, ulong?>
{
    private readonly ulong _privateTextChannelCategoryId =
        config.GetValue<ulong>(ConfigKeys.MemberPrivateChannelsCategoryIdConfigurationKey);

    private readonly string _privateChannelsDescription =
        config.GetValue<string>(ConfigKeys.MemberPrivateChannelsDescriptionConfigurationKey)!;

    public async Task<ulong?> Handle(CreateMemberPrivateChannelCommand request, CancellationToken cancellationToken)
    {
        var clubMember = request.ClubMember;
        LogCreatingPrivateChannel(logger, clubMember.User.Nickname);

        var textChannelName = $"{clubMember.User.Nickname.ToLowerInvariant()}-private-channel";

        var textChannelId = await discordTextChannelAccess.CreatePrivateTextChannelAsync(
                _privateTextChannelCategoryId,
                textChannelName,
                _privateChannelsDescription,
                [clubMember.User.DiscordUserId!.Value],
                null,
                cancellationToken)
            .ConfigureAwait(false);

        if (textChannelId is null)
        {
            LogPrivateTextChannelCouldNotBeCreatedForClubMember(logger, clubMember.User.Nickname);
            return null;
        }

        await SendWelcomeMessageAsync(clubMember, textChannelId.Value, cancellationToken).ConfigureAwait(false);

        var trackedMember = await clubMembers
            .ReadForUpdateByUserIdAsync(clubMember.UserId, cancellationToken)
            .ConfigureAwait(false);
        trackedMember?.SetPrivateTextChannelId(textChannelId.Value);

        LogPrivateChannelCreated(logger, clubMember.User.Nickname, textChannelId.Value);
        return textChannelId;
    }

    private async Task SendWelcomeMessageAsync(ClubMember clubMember, ulong textChannelId, CancellationToken cancellationToken)
    {
        var messageBody = $"Welcome <@{clubMember.User.DiscordUserId!.Value}>! This is your " +
                          "private space to talk to our admins. Only you and the admins can see the messages in this " +
                          "text channel. Use this channel for example to talk about when you need an excuse for the " +
                          "club XP rule or any other concerns you might have.";

        await discordMessageAccess.SendMessageAsync(messageBody, textChannelId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Warning, "Private text channel could not be created for club member '{clubMemberNickname}'")]
    static partial void LogPrivateTextChannelCouldNotBeCreatedForClubMember(ILogger<CreateMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Creating private text channel for club member '{clubMemberNickname}'...")]
    static partial void LogCreatingPrivateChannel(ILogger<CreateMemberPrivateChannelHandler> logger, string clubMemberNickname);

    [LoggerMessage(LogLevel.Information, "Private text channel {TextChannelId} created for club member '{clubMemberNickname}'.")]
    static partial void LogPrivateChannelCreated(ILogger<CreateMemberPrivateChannelHandler> logger, string clubMemberNickname, ulong textChannelId);
}
