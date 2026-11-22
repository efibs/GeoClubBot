using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.CountryChallenges;

namespace GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;

[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("country-challenges", Emoji = "🗺️", Title = "Country challenges", Order = 70,
    ShownWhen = HelpToggle.CountryChallenges,
    Summary = "Themed country challenges on fixed days, with points for the top places and a leaderboard.")]
[HelpSection(1, "How it works", """
    On fixed days I post themed challenges, like *Mongolia Monday*, as GeoGuessr challenge links; finish all five rounds to count. A day or so later I post the results: the top places earn points (usually 3, 2 and 1) and sometimes a role. The points add up to a leaderboard I post regularly.
    """)]
[Group("country-challenges", "The country challenges and their leaderboard")]
public class CountryChallengesModule(
    ISender mediator,
    ILogger<CountryChallengesModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("leaderboard", "The season's leaderboard and your own place on it")]
    [HelpDetail("Ties share a place. Your place, and a winner's role, need your account linked.")]
    public Task LeaderboardAsync() =>
        ExecuteAsync(
            async ct =>
            {
                var result = await Mediator
                    .Send(new GetCountryChallengeLeaderboardQuery(Context.User.Id), ct)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    // The file's problems are for admins; the preview command shows them.
                    var message = result.Error.Code == LoadCountryChallengePlanHandler.InvalidConfigurationCode
                        ? "The country challenges are not set up correctly right now. Please let an admin know."
                        : FriendlyMessageFor(result.Error);
                    await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                foreach (var part in CountryChallengesFormatter.Leaderboard(result.Value))
                {
                    await FollowupAsync(part, ephemeral: true, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
                }
            },
            ephemeral: true,
            failureMessage: "Failed to read the country challenge leaderboard. Please try again later.");
}
