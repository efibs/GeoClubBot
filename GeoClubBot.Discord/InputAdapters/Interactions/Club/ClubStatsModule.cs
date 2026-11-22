using Configuration;
using Constants;
using Discord;
using Discord.Interactions;
using Entities;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Club;
using UseCases.UseCases.MissionBoard;
using UseCases.UseCases.MissionBoardNotifications;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Club;

[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("club", Emoji = "🏆", Title = "Club stats", Order = 50,
    Summary = "How the club is doing today, and this week's mission boards.")]
[Group("club-stats", "How a club is doing today, and its mission boards")]
public partial class ClubStatsModule(
    ISender mediator,
    IOptions<MissionBoardNotificationsConfiguration> boardAlertsConfig,
    ILogger<ClubStatsModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    private const string BoardAlertsFailureMessage =
        "Failed to change your board alerts. Please try again later. If the issue persists, please contact an admin.";

    [SlashCommand("todays-xp", "A club's XP so far today, streaks kept, missions finished and claimed")]
    [HelpDetail("Today is the UTC day; claims count in the current claim cycle.")]
    public Task GetTodaysXpAsync(
        [Autocomplete(typeof(ClubAutocompleteHandler))][Summary(description: "The club; the main club if left out")] string? club = null) =>
        ExecuteAsync(
            async ct =>
            {
                if (!ClubOption.TryParse(club, out var clubId))
                {
                    await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var result = await Mediator
                    .Send(new GetClubTodaysXpQuery(clubId), ct)
                    .ConfigureAwait(false);

                if (result.ClubName is null)
                {
                    await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                // Separate counts: the streak and the mission board are independent XP sources, so
                // one number would hide half the picture.
                var claims = result.ClaimMemberCount is { } claimCount
                    ? $" · Claimed a mission this claim cycle: {claimCount}/{result.TotalMemberCount}"
                    : string.Empty;
                await FollowupAsync(
                        $"{result.ClubName} currently has {result.Xp} XP today (UTC). "
                        + $"Streak: {result.ChallengeMemberCount}/{result.TotalMemberCount} · "
                        + $"Missions finished: {result.BoardMissionCount}"
                        + claims,
                        ephemeral: false)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to fetch the clubs current XP. Please try again later. If the issue persists, please contact an admin.");

    [SlashCommand("board", "This week's club mission boards and who holds which open mission")]
    [HelpDetail("When the week ends and claims reset, each board (✅ cleared, ▶️ current, 🔒 locked), and every open mission: who holds it, its progress, when it was claimed, and 🆘 if they asked for help.")]
    public Task GetMissionBoardAsync(
        [Summary(description: "The club; the main club if left out")]
        [Autocomplete(typeof(ClubAutocompleteHandler))] string? club = null) =>
        ExecuteAsync(
            async ct =>
            {
                if (!ClubOption.TryParse(club, out var clubId))
                {
                    await FollowupAsync(ClubOption.UnknownClubMessage, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var result = await Mediator
                    .Send(new GetClubMissionBoardQuery(clubId), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    await FollowupFailureAsync(result.Error).ConfigureAwait(false);
                    return;
                }

                // The button opens each member's own alerts, privately.
                var components = boardAlertsConfig.Value.Enabled
                    ? new ComponentBuilder()
                        .WithButton(MissionBoardAlertsPanel.ButtonLabel, ComponentIds.MissionBoardAlertsButtonId,
                            ButtonStyle.Secondary, new Emoji("🔔"))
                        .Build()
                    : null;

                // Nicknames only, in an embed: nobody is pinged by someone checking the board.
                await FollowupAsync(
                        embed: ClubMissionBoardFormatter.BuildEmbed(result.Value).Build(),
                        components: components,
                        allowedMentions: AllowedMentions.None)
                    .ConfigureAwait(false);
            },
            failureMessage: "Failed to read the club's mission board. Please try again later. If the issue persists, please contact an admin.");
    [SlashCommand("board-alerts", "Get a DM when your club's next mission board is unlocked or a new week starts")]
    [HelpShownWhen(HelpToggle.MissionBoardNotifications)]
    [HelpDetail("""
        So you can pick an easy mission before others take them. Your club is the one your linked GeoGuessr account is in, so link it first (`/gg-account link`).
        Pick **every new board**, **only the next one** (the alerts then stop by themselves) or **off**. No DM is sent while you have already claimed this claim cycle, since you couldn't claim anyway. The 🔔 button under `/club-stats board` and the 🔕 button under each DM do the same.
        """)]
    public Task SetBoardAlertsAsync(
        [Summary("alerts", "Every new board, only the next one, or off")] BoardAlertsChoice alerts) =>
        ExecuteAsync(
            async ct =>
            {
                var outcome = await ApplyBoardAlertsAsync(alerts, ct).ConfigureAwait(false);
                await FollowupAsync(outcome.Message, ephemeral: true).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: BoardAlertsFailureMessage);

    /// <summary>The 🔔 button under <c>/club-stats board</c>: the clicking member's own alerts, privately.</summary>
    [ComponentInteraction(ComponentIds.MissionBoardAlertsButtonId, ignoreGroupNames: true)]
    public async Task ShowBoardAlertsPanelAsync()
    {
        try
        {
            if (!boardAlertsConfig.Value.Enabled)
            {
                await RespondAsync("Mission board alerts are switched off.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var mode = await Mediator.Send(new GetMissionBoardSubscriptionQuery(Context.User.Id)).ConfigureAwait(false);
            var (text, components) = MissionBoardAlertsPanel.Build(mode);
            await RespondAsync(text, components: components, ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBoardAlertsButtonFailed(Logger, ex);
            await TryRespondAsync(BoardAlertsFailureMessage).ConfigureAwait(false);
        }
    }

    /// <summary>A choice on the private panel: applied, and the panel shows the new state.</summary>
    [ComponentInteraction($"{ComponentIds.MissionBoardAlertsSetButtonId}:*", ignoreGroupNames: true)]
    public async Task SetBoardAlertsFromPanelAsync(string choiceKey)
    {
        try
        {
            if (MissionBoardAlertsPanel.ParseChoiceKey(choiceKey) is not { } choice)
            {
                await RespondAsync(BoardAlertsFailureMessage, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var outcome = await ApplyBoardAlertsAsync(choice, CancellationToken.None).ConfigureAwait(false);
            var mode = await Mediator.Send(new GetMissionBoardSubscriptionQuery(Context.User.Id)).ConfigureAwait(false);
            var (text, components) = MissionBoardAlertsPanel.Build(mode, outcome.Succeeded ? null : $"⚠️ {outcome.Message}");

            await ((IComponentInteraction)Context.Interaction).UpdateAsync(properties =>
            {
                properties.Content = text;
                properties.Components = components;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBoardAlertsButtonFailed(Logger, ex);
            await TryRespondAsync(BoardAlertsFailureMessage).ConfigureAwait(false);
        }
    }

    /// <summary>The 🔕 button under a board alert DM.</summary>
    [ComponentInteraction(ComponentIds.MissionBoardUnsubscribeButtonId, ignoreGroupNames: true)]
    public async Task UnsubscribeFromDirectMessageAsync()
    {
        try
        {
            var removed = await Mediator.Send(new UnsubscribeFromMissionBoardCommand(Context.User.Id)).ConfigureAwait(false);

            // The button has done its job; the DM's text stays.
            await ((IComponentInteraction)Context.Interaction)
                .UpdateAsync(properties => properties.Components = new ComponentBuilder().Build())
                .ConfigureAwait(false);
            await FollowupAsync(removed
                    ? "🔕 Board alerts are off. Use `/club-stats board-alerts` to turn them on again."
                    : "🔕 You have no board alerts.")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBoardAlertsButtonFailed(Logger, ex);
            await TryRespondAsync(BoardAlertsFailureMessage).ConfigureAwait(false);
        }
    }

    private async Task<(bool Succeeded, string Message)> ApplyBoardAlertsAsync(BoardAlertsChoice choice, CancellationToken cancellationToken)
    {
        if (MissionBoardAlertsPanel.ModeOf(choice) is not { } mode)
        {
            var removed = await Mediator.Send(new UnsubscribeFromMissionBoardCommand(Context.User.Id), cancellationToken).ConfigureAwait(false);
            return (true, removed ? MissionBoardAlertsPanel.Describe(null) : "🔕 You had no board alerts.");
        }

        var result = await Mediator
            .Send(new SubscribeToMissionBoardCommand(Context.User.Id, mode), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? (false, FriendlyMessageFor(result.Error))
            : (true, $"{MissionBoardAlertsPanel.Describe(mode, result.Value)}\n{MissionBoardAlertsPanel.SkipNote}");
    }

    private async Task TryRespondAsync(string message)
    {
        try
        {
            if (Context.Interaction.HasResponded)
            {
                await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(message, ephemeral: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogBoardAlertsButtonFailed(Logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "A board alerts button failed")]
    static partial void LogBoardAlertsButtonFailed(ILogger logger, Exception ex);
}
