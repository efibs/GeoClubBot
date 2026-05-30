using Configuration;
using Discord.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Strikes;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        ISender mediator,
        ILogger<ActivityStrikeModule> logger,
        IOptions<ActivityCheckerConfiguration> activityCheckerOptions) : ClubBotInteractionModule(mediator, logger)
    {
        private readonly TimeSpan _strikeDecayTimeSpan = activityCheckerOptions.Value.StrikeDecayTimeSpan;

        [SlashCommand("add", "Create a new strike for a player")]
        public async Task CreateStrikeAsync(string memberNickname,
            [Summary(description: "Strike date in format YYYY-MM-DD")]
            DateTime strikeDate)
        {
            strikeDate = DateTime.SpecifyKind(strikeDate, DateTimeKind.Utc);

            var result = await Mediator
                .Send(new AddStrikeCommand(memberNickname, strikeDate))
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync($"Strike could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                var expirationTimeSpan = _strikeDecayTimeSpan;
                await RespondAsync(
                    $"Strike with id {result.Value} was added to player **{memberNickname}** (expires: {(strikeDate + expirationTimeSpan):d}).")
                    .ConfigureAwait(false);
            }
        }

        [SlashCommand("read", "Read the strikes a player currently has")]
        public async Task ReadStrikesAsync(string memberNickname)
        {
            var result = await Mediator
                .Send(new ReadMemberStrikesQuery(memberNickname))
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            var strikeStatus = result.Value;

            if (strikeStatus.Strikes.Count == 0)
            {
                await RespondAsync($"The player {memberNickname} currently has no strikes.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var expirationTimeSpan = _strikeDecayTimeSpan;
            var strikesListString = string.Join("\n", strikeStatus.Strikes.Select(s =>
                $"- {s.Timestamp:d} - Revoked: {s.Revoked} (Id: {s.StrikeId}, expires: {(s.Timestamp + expirationTimeSpan):d})"));

            await RespondAsync(
                $"The player {memberNickname} currently has {strikeStatus.NumActiveStrikes} active strikes:\n{strikesListString}",
                ephemeral: true).ConfigureAwait(false);
        }

        [SlashCommand("read-all", "Read all strikes currently in the system")]
        public async Task ReadAllStrikesAsync()
        {
            var strikes = await Mediator.Send(new ReadAllStrikesQuery()).ConfigureAwait(false);

            if (strikes.Count == 0)
            {
                await RespondAsync("There are currently no strikes in the system.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var expirationTimeSpan =
                _strikeDecayTimeSpan;

            var strikesListString = string.Join("\n", strikes
                .OrderBy(s => s.ClubMember!.User!.Nickname)
                .ThenBy(s => s.Timestamp)
                .Select(s => $"- {s.ToStringDetailed(expirationTimeSpan)}"));

            if (strikesListString.Length > 1500)
            {
                var stream = new MemoryStream();
                var writer = new StreamWriter(stream);
                await writer.WriteAsync(strikesListString).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                stream.Position = 0;

                await RespondWithFileAsync(stream, fileName: "strikes.txt",
                    text: "The strikes currently in the system are:", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync($"The strikes currently in the system are: \n{strikesListString}",
                    ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("read-relevant", "Read all strikes that are currently relevant")]
        public Task ReadAllRelevantStrikesAsync() =>
            ExecuteAsync(
                async ct =>
                {
                    var strikes = await Mediator
                        .Send(new ReadAllRelevantStrikesQuery(), ct)
                        .ConfigureAwait(false);

                    if (strikes.Count == 0)
                    {
                        await FollowupAsync("There are currently no relevant strikes in the system.",
                                ephemeral: true)
                            .ConfigureAwait(false);
                        return;
                    }

                    var strikesListString = string.Join("\n", strikes
                        .OrderByDescending(s => s.NumActiveStrikes)
                        .ThenBy(s => s.MemberNickname)
                        .Select(s => $"- {s}"));

                    if (strikesListString.Length > 1500)
                    {
                        var stream = new MemoryStream();
                        var writer = new StreamWriter(stream);
                        await writer.WriteAsync(strikesListString).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                        stream.Position = 0;

                        await FollowupWithFileAsync(stream, fileName: "strikes.txt",
                            text: "The currently relevant strikes are:", ephemeral: true).ConfigureAwait(false);
                    }
                    else
                    {
                        await FollowupAsync($"The currently relevant strikes are: \n{strikesListString}",
                                ephemeral: true)
                            .ConfigureAwait(false);
                    }
                },
                ephemeral: true,
                failureMessage: "Reading relevant strikes failed: Internal error. Try again later. If the problem persists, please contact an admin.");

        [SlashCommand("revoke", "Revoke a strike")]
        public async Task RevokeStrikeAsync(string strikeId)
        {
            if (!Guid.TryParse(strikeId, out var strikeIdGuid))
            {
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator.Send(new RevokeStrikeCommand(strikeIdGuid)).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            var strike = result.Value;
            var nickname = strike.ClubMember?.User.Nickname ?? "Unknown";
            var expirationTimeSpan = _strikeDecayTimeSpan;
            await RespondAsync(
                $"Strike for player **{nickname}** from {strike.Timestamp:d} (expires: {(strike.Timestamp + expirationTimeSpan):d}, id: {strikeId}) was successfully revoked.")
                .ConfigureAwait(false);
        }

        [SlashCommand("unrevoke", "Remove a revocation of a strike")]
        public async Task UnrevokeStrikeAsync(string strikeId)
        {
            if (!Guid.TryParse(strikeId, out var strikeIdGuid))
            {
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var result = await Mediator.Send(new UnrevokeStrikeCommand(strikeIdGuid)).ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RespondAsync(FriendlyMessageFor(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            var strike = result.Value;
            var nickname = strike.ClubMember?.User.Nickname ?? "Unknown";
            var expirationTimeSpan = _strikeDecayTimeSpan;
            await RespondAsync(
                $"Revocation of strike for player **{nickname}** from {strike.Timestamp:d} (expires: {(strike.Timestamp + expirationTimeSpan):d}, id: {strikeId}) was successfully removed.")
                .ConfigureAwait(false);
        }
    }
}
