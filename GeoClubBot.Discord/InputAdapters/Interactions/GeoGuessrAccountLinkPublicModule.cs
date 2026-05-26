using System.Text.RegularExpressions;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("gg-account", "Commands for linking Discord accounts to GeoGuessr accounts")]
public class GeoGuessrAccountLinkPublicModule(
    ISender mediator,
    ILogger<GeoGuessrAccountLinkPublicModule> logger,
    IConfiguration config) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("link", "Link your Discord account to a GeoGuessr account")]
    public Task StartLinkingProcessAsync([Summary(description: "The link to your profile")] string shareProfileLink) =>
        ExecuteAsync(
            async ct =>
            {
                var executingUser = Context.User as SocketGuildUser;

                if (!_tryParseUserIdFromProfileLink(shareProfileLink, out var geoGuessrUserId) || geoGuessrUserId is null)
                {
                    await FollowupAsync("Account link failed: The profile link was in the wrong format.\n\n" +
                                        "The link should look something like this: https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f. " +
                                        "You can retrieve the link from your profile page: In the top right of GeoGuessr go to \"Profile\". " +
                                        "Then click the share button left of 'EDIT AVATAR'. There you can copy your profile link.\n\nPlease try again.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var linkedDiscordUserId = await Mediator
                    .Send(new GetLinkedDiscordUserIdQuery(geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                if (linkedDiscordUserId.HasValue)
                {
                    var linkedDiscordUser = Context.Guild.GetUser(linkedDiscordUserId.Value);
                    await FollowupAsync("Account link failed: The given GeoGuessr account is already linked to Discord account " +
                                        $"\"{linkedDiscordUser.DisplayName}\". Please contact an admin if you think this is a mistake.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var linkedGeoGuessrUser = await Mediator
                    .Send(new GetLinkedGeoGuessrUserQuery(executingUser!.Id), ct)
                    .ConfigureAwait(false);

                if (linkedGeoGuessrUser != null)
                {
                    await FollowupAsync("Account link failed: This Discord account is already linked to GeoGuessr account " +
                                        $"\"{linkedGeoGuessrUser}\". Please contact an admin if you think this is a mistake.",
                            ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                var linkingRequest = await Mediator
                    .Send(new GetOpenAccountLinkingRequestQuery(executingUser.Id), ct)
                    .ConfigureAwait(false);

                if (linkingRequest is not null)
                {
                    if (geoGuessrUserId == linkingRequest.GeoGuessrUserId)
                    {
                        await FollowupAsync($"You already started an account linking for this account. Here is the one " +
                                            $"time password again in case you lost it: **{linkingRequest.OneTimePassword}**",
                                ephemeral: true)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await FollowupAsync("Account link failed: You already started an account linking process for " +
                                            $"another GeoGuessr account (Id: {linkingRequest.GeoGuessrUserId}) that needs to be completed or canceled first. \n\n" +
                                            "To complete it: Send the one time password to an admin in GeoGuessr.\n" +
                                            "To cancel it: Ask an admin to cancel the process for you.",
                                ephemeral: true)
                            .ConfigureAwait(false);
                    }
                    return;
                }

                var oneTimePassword = await Mediator
                    .Send(new StartAccountLinkingCommand(executingUser.Id, geoGuessrUserId), ct)
                    .ConfigureAwait(false);

                if (oneTimePassword is null)
                {
                    await FollowupAsync("Account link failed: Linking process already started. " +
                                        "If you have not received a one time password, then please contact an admin. " +
                                        "If you have already sent the one time password to an admin in GeoGuessr, lean back " +
                                        "and relax while the admins are processing your request.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                await FollowupAsync("**IMPORTANT! READ CAREFULLY!**: Account linking process successfully started. To complete the linking process, " +
                                    "please send the following one time password to an admin of this Discord server as a direct message **in GeoGuessr**. Do not send " +
                                    "the password here in Discord or anywhere else other than as a direct message in GeoGuessr.\n" +
                                    $"Here is your one time password: **{oneTimePassword}**\n\n" +
                                    "The admin will then complete your request and you will get notified.", ephemeral: true).ConfigureAwait(false);

                await _sendAdminAccountLinkingStartedMessageAsync(executingUser, geoGuessrUserId).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Account link failed: Internal error. Try again later. If the problem persists, please contact an admin.");

    private bool _tryParseUserIdFromProfileLink(string profileLink, out string? userId)
    {
        userId = null;

        if (!ShareProfileLinkCheckerRegex.IsMatch(profileLink))
        {
            return false;
        }

        var splitIndex = profileLink.LastIndexOf('/');
        userId = profileLink[(splitIndex + 1)..];
        return true;
    }

    private async Task _sendAdminAccountLinkingStartedMessageAsync(SocketGuildUser executingUser, string geoGuessrUserId)
    {
        var adminTextChannel = Context.Guild.GetTextChannel(_accountLinkingAdminChannelId);

        var completeButtonId = $"{ComponentIds.GeoGuessrAccountLinkingCompleteButtonId}:{executingUser.Id},{geoGuessrUserId}";
        var cancelButtonId = $"{ComponentIds.GeoGuessrAccountLinkingCancelButtonId}:{executingUser.Id},{geoGuessrUserId}";

        var completeButton = new ComponentBuilder()
            .WithButton("Complete", completeButtonId)
            .WithButton("Cancel", cancelButtonId, style: ButtonStyle.Danger)
            .Build();

        await adminTextChannel
            .SendMessageAsync($"User {executingUser.DisplayName} (id: {executingUser.Id}) " +
                              $"started the linking process for GeoGuessr account {geoGuessrUserId}. " +
                              $"Click the button below to complete the linking process.\n\n" +
                              $"**Only accept the password if it was sent to you by the correct user inside GeoGuessr!**",
                components: completeButton).ConfigureAwait(false);
    }

    private static readonly Regex ShareProfileLinkCheckerRegex =
        new(@"^https:\/\/www\.geoguessr\.com\/user\/[\da-z]{24}$", RegexOptions.Compiled);

    private readonly ulong _accountLinkingAdminChannelId =
        config.GetValue<ulong>(ConfigKeys.GeoGuessrAccountLinkingAdminChannelIdConfigurationKey);
}
