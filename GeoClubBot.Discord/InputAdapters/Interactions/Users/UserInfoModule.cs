using System.Globalization;
using Discord;
using Discord.Interactions;
using Extensions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.RankedSystem;
using Utilities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Users;

[CommandContextType(InteractionContextType.Guild)]
[Group("user-info", "Commands for getting information about a user")]
public class UserInfoModule(
    ISender mediator,
    ILogger<UserInfoModule> logger) : ClubBotInteractionModule(mediator, logger)
{
    [SlashCommand("gg-nickname", "Get the GeoGuessr nickname of a user")]
    [UserCommand("GeoGuessr Nickname")]
    public Task GetGeoGuessrNicknameAsync(IGuildUser user) =>
        ExecuteAsync(
            async ct =>
            {
                var linkedGeoGuessrAccount = await Mediator
                    .Send(new GetLinkedGeoGuessrUserQuery(user.Id), ct)
                    .ConfigureAwait(false);

                if (linkedGeoGuessrAccount.IsFailure)
                {
                    await FollowupAsync($"The user '{user.DisplayName}' has not linked his GeoGuessr account yet.", ephemeral: true)
                        .ConfigureAwait(false);
                }
                else
                {
                    await FollowupAsync($"The user '{user.DisplayName}' is called '**{linkedGeoGuessrAccount.Value.Nickname}**' in GeoGuessr.", ephemeral: true)
                        .ConfigureAwait(false);
                }
            },
            ephemeral: true,
            failureMessage: "Reading the GeoGuessr nickname failed. Try again later. If the problem persists, please contact an admin.");

    [SlashCommand("gg-profile", "Get the GeoGuessr profile of a user")]
    [UserCommand("GeoGuessr Profile")]
    public Task GetGeoGuessrProfileAsync(IGuildUser user) =>
        ExecuteAsync(
            async ct =>
            {
                var profile = await Mediator
                    .Send(new GetGeoGuessrProfileQuery(user.Id), ct)
                    .ConfigureAwait(false);

                if (profile.IsFailure)
                {
                    await FollowupAsync(
                        $"The user '{user.DisplayName}' has not linked their GeoGuessr account yet.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var rankedProgress = await Mediator
                    .Send(new GetUserRankedProgressQuery(user.Id), ct)
                    .ConfigureAwait(false);

                var peakRating = await Mediator
                    .Send(new GetUserRankedPeakRatingQuery(user.Id), ct)
                    .ConfigureAwait(false);

                await FollowupAsync(embed: BuildProfileEmbed(profile.Value, rankedProgress, peakRating), ephemeral: true).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Reading the GeoGuessr profile failed. Try again later. If the problem persists, please contact an admin.");

    [SlashCommand("gg-ranked", "Get the GeoGuessr ranked statistics of a user")]
    [UserCommand("GeoGuessr Ranked Stats")]
    public Task GetGeoGuessrRankedStatsAsync(IGuildUser user) =>
        ExecuteAsync(
            async ct =>
            {
                var profile = await Mediator
                    .Send(new GetGeoGuessrProfileQuery(user.Id), ct)
                    .ConfigureAwait(false);

                if (profile.IsFailure)
                {
                    await FollowupAsync(
                        $"The user '{user.DisplayName}' has not linked their GeoGuessr account yet.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var rankedProgress = await Mediator
                    .Send(new GetUserRankedProgressQuery(user.Id), ct)
                    .ConfigureAwait(false);

                var peakRating = await Mediator
                    .Send(new GetUserRankedPeakRatingQuery(user.Id), ct)
                    .ConfigureAwait(false);

                if (rankedProgress.IsFailure && peakRating.IsFailure)
                {
                    await FollowupAsync(
                        $"The user '{user.DisplayName}' has no ranked statistics yet.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }

                await FollowupAsync(embed: BuildRankedStatsEmbed(profile.Value, rankedProgress, peakRating), ephemeral: true).ConfigureAwait(false);
            },
            ephemeral: true,
            failureMessage: "Reading the GeoGuessr ranked statistics failed. Try again later. If the problem persists, please contact an admin.");

    [SlashCommand("discord-user", "Get the Discord user for a GeoGuessr nickname")]
    public Task GetDiscordUserAsync(
        [Autocomplete(typeof(LinkedUserNicknameAutocompleteHandler))] string nickname) =>
        ExecuteAsync(
            async ct =>
            {
                var discordUserId = await Mediator
                    .Send(new GetDiscordUserByNicknameQuery(nickname), ct)
                    .ConfigureAwait(false);

                if (discordUserId.IsFailure)
                {
                    await FollowupAsync($"No linked Discord user found for GeoGuessr player '**{nickname}**'.", ephemeral: true)
                        .ConfigureAwait(false);
                }
                else
                {
                    await FollowupAsync($"The GeoGuessr player '**{nickname}**' is <@{discordUserId.Value}>.", ephemeral: true)
                        .ConfigureAwait(false);
                }
            },
            ephemeral: true,
            failureMessage: "Reading the Discord user failed. Try again later. If the problem persists, please contact an admin.");

    internal static Embed BuildProfileEmbed(UserDto profile, Result<RankedProgressResponseDto> rankedProgress, Result<RankedPeakRatingResponseDto> rankedPeakRating)
    {
        var flagEmoji = profile.CountryCode.ToFlagEmoji();

        var profileUrl = $"https://www.geoguessr.com/user/{profile.Id}";
        var thumbnailUrl = string.IsNullOrWhiteSpace(profile.CustomImage)
            ? null
            : $"https://www.geoguessr.com/images/resize:auto:96:96/gravity:ce/plain/{profile.CustomImage}";

        var hasRating = rankedProgress.ValueOrNull?.Rating is not null;

        var ratingDisplay = rankedProgress.ValueOrNull?.Rating?.ToString()
                            ?? rankedPeakRating.ValueOrNull?.PeakOverallRating?.ToString()
                            ?? "N/A";

        string statusDisplay;
        if (profile.IsBanned)
            statusDisplay = "Banned";
        else if (profile.SuspendedUntil.HasValue && profile.SuspendedUntil.Value > DateTimeOffset.UtcNow)
            statusDisplay = $"Suspended until <t:{profile.SuspendedUntil.Value.ToUnixTimeSeconds()}:f>";
        else if (profile.ChatBan)
            statusDisplay = "Chat banned";
        else
            statusDisplay = "Good standing";

        var embed = new EmbedBuilder()
            .WithTitle($"{flagEmoji} {profile.Nick}")
            .WithUrl(profileUrl)
            .WithColor(new Color(0x1A, 0xBC, 0x9C));

        if (thumbnailUrl is not null)
            embed.WithThumbnailUrl(thumbnailUrl);

        embed
            .AddField("Member since", $"<t:{profile.Created.ToUnixTimeSeconds()}:D>", inline: true)
            .AddField("Type", profile.IsProUser ? $"{profile.Type} (Pro)" : profile.Type, inline: true)
            .AddField("Level", profile.Progress?.Level.ToString() ?? "–", inline: true)
            .AddField(hasRating ? "Rating" : "Peak rating", ratingDisplay, inline: true)
            .AddField("Status", statusDisplay, inline: true);

        if (profile.Club is not null)
        {
            var clubUrl = $"https://www.geoguessr.com/clubs/{profile.Club.ClubId}";
            embed.AddField("Club", $"[{profile.Club.Tag}]({clubUrl}) (lvl {profile.Club.Level})", inline: true);
        }

        return embed.Build();
    }

    internal static Embed BuildRankedStatsEmbed(UserDto profile, Result<RankedProgressResponseDto> rankedProgress, Result<RankedPeakRatingResponseDto> rankedPeakRating)
    {
        const string na = "N/A";

        var flagEmoji = profile.CountryCode.ToFlagEmoji();
        var profileUrl = $"https://www.geoguessr.com/user/{profile.Id}";
        var thumbnailUrl = string.IsNullOrWhiteSpace(profile.CustomImage)
            ? null
            : $"https://www.geoguessr.com/images/resize:auto:96:96/gravity:ce/plain/{profile.CustomImage}";

        var progress = rankedProgress.ValueOrNull;
        var peak = rankedPeakRating.ValueOrNull;

        var division = string.IsNullOrWhiteSpace(progress?.DivisionName) ? na : progress.DivisionName;
        var winStreak = progress is not null ? progress.WinStreak.ToString() : na;
        var guessedFirst = progress is not null
            ? (progress.GuessedFirstRate * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
            : na;

        var recentGames = progress?.LatestGames is { Count: > 0 } games
            ? string.Concat(games.Select(won => won ? "🟩" : "🟥"))
            : na;

        var embed = new EmbedBuilder()
            .WithTitle($"{flagEmoji} {profile.Nick}")
            .WithUrl(profileUrl)
            .WithColor(new Color(0x1A, 0xBC, 0x9C));

        if (thumbnailUrl is not null)
            embed.WithThumbnailUrl(thumbnailUrl);

        embed
            .AddField("Division", division, inline: true)
            .AddField("Win streak", winStreak, inline: true)
            .AddField("Guessed first", guessedFirst, inline: true)
            .AddField("Current rating", FormatRatingBlock(progress?.Rating, progress?.GameModeRatings), inline: true)
            .AddField("Peak rating", FormatRatingBlock(peak?.PeakOverallRating, peak?.PeakGameModeRatings), inline: true)
            .AddField("Recent games", recentGames, inline: false)
            .AddField("Best countries", FormatCountryFlags(progress?.BestCountries), inline: true)
            .AddField("Worst countries", FormatCountryFlags(progress?.WorstCountries), inline: true);

        return embed.Build();
    }

    private static string FormatRatingBlock(int? overall, GameModeRatingsDto? gameModes)
    {
        static string Rating(int? value) => value?.ToString() ?? "N/A";

        return $"Overall: {Rating(overall)}\n" +
               $"Move: {Rating(gameModes?.MoveDuels)}\n" +
               $"No Move: {Rating(gameModes?.NoMoveDuels)}\n" +
               $"NMPZ: {Rating(gameModes?.NmpzDuels)}";
    }

    private static string FormatCountryFlags(List<string>? countryCodes)
    {
        if (countryCodes is not { Count: > 0 })
            return "N/A";

        return string.Join(" ", countryCodes.Select(code =>
        {
            var flag = code.ToFlagEmoji();
            return string.IsNullOrEmpty(flag) ? code : flag;
        }));
    }
}
