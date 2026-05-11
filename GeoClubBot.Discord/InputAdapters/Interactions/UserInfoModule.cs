using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

[CommandContextType(InteractionContextType.Guild)]
[Group("user-info", "Commands for getting information about a user")]
public partial class UserInfoModule(IGetLinkedGeoGuessrUserUseCase getLinkedGeoGuessrUserUseCase,
    IGetDiscordUserByNicknameUseCase getDiscordUserByNicknameUseCase,
    IGetGeoGuessrProfileUseCase getGeoGuessrProfileUseCase,
    ILogger<UserInfoModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("gg-nickname", "Get the GeoGuessr nickname of a user")]
    [UserCommand("gg-nickname")]
    public async Task GetGeoGuessrNicknameAsync(IGuildUser user)
    {
        try
        {
            // Defer the response
            await DeferAsync(ephemeral: true).ConfigureAwait(false);
            
            // Get the linked account
            var linkedGeoGuessrAccount = await getLinkedGeoGuessrUserUseCase
                .GetLinkedGeoGuessrUserAsync(user.Id)
                .ConfigureAwait(false);

            // If the user is not linked
            if (linkedGeoGuessrAccount == null)
            {
                // Send response
                await FollowupAsync($"The user '{user.DisplayName}' has not linked his GeoGuessr account yet.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                // Send response
                await FollowupAsync($"The user '{user.DisplayName}' is called '**{linkedGeoGuessrAccount.Nickname}**' in GeoGuessr.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Log error
            LogReadingTheGeoguessrNicknameOfUserFailed(logger, ex, user.DisplayName);
            
            // Respond with error message
            await FollowupAsync("Reading the GeoGuessr nickname failed. Try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error, "Reading the GeoGuessr nickname of the user '{userDisplayName}' failed.")]
    static partial void LogReadingTheGeoguessrNicknameOfUserFailed(ILogger<UserInfoModule> logger, Exception ex, string userDisplayName);

    [SlashCommand("gg-profile", "Get the GeoGuessr profile of a user")]
    [UserCommand("gg-profile")]
    public async Task GetGeoGuessrProfileAsync(IGuildUser user)
    {
        try
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            var profile = await getGeoGuessrProfileUseCase
                .GetGeoGuessrProfileAsync(user.Id)
                .ConfigureAwait(false);

            if (profile is null)
            {
                await FollowupAsync(
                    $"The user '{user.DisplayName}' has not linked their GeoGuessr account yet.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: BuildProfileEmbed(profile), ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogReadingTheGeoguessrProfileOfUserFailed(logger, ex, user.DisplayName);
            await FollowupAsync(
                "Reading the GeoGuessr profile failed. Try again later. If the problem persists, please contact an admin.",
                ephemeral: true).ConfigureAwait(false);
        }
    }

    private static Embed BuildProfileEmbed(UserDto profile)
    {
        // ISO 3166-1 alpha-2 → regional indicator emoji pair (e.g. "DE" → 🇩🇪)
        var flagEmoji = string.IsNullOrWhiteSpace(profile.CountryCode)
            ? string.Empty
            : string.Concat(profile.CountryCode.ToUpperInvariant()
                  .Select(c => char.ConvertFromUtf32(0x1F1E6 + (c - 'A'))));

        var profileUrl = $"https://www.geoguessr.com/user/{profile.Id}";
        var thumbnailUrl = string.IsNullOrWhiteSpace(profile.CustomImage)
            ? null
            : $"https://www.geoguessr.com/images/auto/{profile.CustomImage}";

        var ratingDisplay = profile.Competitive.Rating == 0
            ? "N/A"
            : profile.Competitive.Rating.ToString();

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
            .AddField("Rating", ratingDisplay, inline: true)
            .AddField("Status", statusDisplay, inline: true);

        if (profile.Club is not null)
        {
            var clubUrl = $"https://www.geoguessr.com/clubs/{profile.Club.ClubId}";
            embed.AddField("Club", $"[{profile.Club.Tag}]({clubUrl}) (lvl {profile.Club.Level})", inline: true);
        }

        return embed.Build();
    }

    [LoggerMessage(LogLevel.Error, "Reading the GeoGuessr profile of the user '{userDisplayName}' failed.")]
    static partial void LogReadingTheGeoguessrProfileOfUserFailed(ILogger<UserInfoModule> logger, Exception ex, string userDisplayName);

    [SlashCommand("discord-user", "Get the Discord user for a GeoGuessr nickname")]
    public async Task GetDiscordUserAsync(string nickname)
    {
        try
        {
            await DeferAsync(ephemeral: true).ConfigureAwait(false);

            var discordUserId = await getDiscordUserByNicknameUseCase
                .GetDiscordUserIdByNicknameAsync(nickname)
                .ConfigureAwait(false);

            if (discordUserId == null)
            {
                await FollowupAsync($"No linked Discord user found for GeoGuessr player '**{nickname}**'.", ephemeral: true)
                    .ConfigureAwait(false);
            }
            else
            {
                await FollowupAsync($"The GeoGuessr player '**{nickname}**' is <@{discordUserId}>.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogReadingTheDiscordUserOfNicknameFailed(logger, ex, nickname);
            await FollowupAsync("Reading the Discord user failed. Try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(LogLevel.Error, "Reading the Discord user for GeoGuessr nickname '{nickname}' failed.")]
    static partial void LogReadingTheDiscordUserOfNicknameFailed(ILogger<UserInfoModule> logger, Exception ex, string nickname);
}