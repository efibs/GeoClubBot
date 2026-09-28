using Discord;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.CountryChallenges;

namespace GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;

[CommandContextType(InteractionContextType.Guild)]
[Group("country-challenges", "The country challenges")]
public class CountryChallengesModule(
    ISender mediator,
    ILogger<CountryChallengesModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("leaderboard", "Show the country challenge leaderboard and your place on it")]
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
