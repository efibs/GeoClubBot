using Constants;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Strikes;

namespace Infrastructure.InputAdapters.Interactions;

public partial class ActivityModule
{
    public partial class ActivityStrikeModule(
        IAddStrikeUseCase addStrikeUseCase,
        IReadMemberStrikesUseCase readMemberStrikesUseCase,
        IReadAllStrikesUseCase readAllStrikesUseCase,
        IReadAllRelevantStrikesUseCase readAllRelevantStrikesUseCase,
        IRevokeStrikeUseCase revokeStrikeUseCase,
        IUnrevokeStrikeUseCase unrevokeStrikeUseCase,
        ILogger<ActivityStrikeModule> logger,
        IConfiguration config)
    {
        [SlashCommand("add", "Create a new strike for a player")]
        public async Task CreateStrikeAsync(string memberNickname,
            [Summary(description: "Strike date in format YYYY-MM-DD")]
            DateTime strikeDate)
        {
            // Specify the date time as utc
            strikeDate = DateTime.SpecifyKind(strikeDate, DateTimeKind.Utc);

            // Add the strike
            var strikeId = await addStrikeUseCase.AddStrikeAsync(memberNickname, strikeDate).ConfigureAwait(false);

            // If the player has a status set
            if (strikeId == null)
            {
                await RespondAsync($"Excuse could not be added for player '{memberNickname}'. Is the nickname wrong?",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(
                    $"Strike with id {strikeId} was added to player **{memberNickname}**.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("read", "Read the strikes a player currently has")]
        public async Task ReadStrikesAsync(string memberNickname)
        {
            // Read the strikes
            var strikeStatus = await readMemberStrikesUseCase.ReadMemberStrikesAsync(memberNickname).ConfigureAwait(false);

            // If the player has a status set
            if (strikeStatus == null)
            {
                // Respond
                await RespondAsync($"There is no player with the nickname {memberNickname} currently being tracked. " +
                                   $"Either the nickname is incorrect or the member just joined and is not yet being tracked.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            // If the player has no strikes
            if (strikeStatus.Strikes.Count == 0)
            {
                // Respond
                await RespondAsync($"The player {memberNickname} currently has no strikes.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Build the list of strikes
            var strikesListString = string.Join("\n", strikeStatus.Strikes.Select(s => $"- {s}"));

            // Respond
            await RespondAsync(
                $"The player {memberNickname} currently has {strikeStatus.NumActiveStrikes} active strikes:\n{strikesListString}",
                ephemeral: true).ConfigureAwait(false);
        }

        [SlashCommand("read-all", "Read all strikes currently in the system")]
        public async Task ReadAllStrikesAsync()
        {
            // Read the strikes
            var strikes = await readAllStrikesUseCase.ReadAllStrikesAsync().ConfigureAwait(false);

            // If the player has a status set
            if (strikes.Count == 0)
            {
                // Respond
                await RespondAsync("There are currently no strikes in the system.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Get the expiration time span
            var expirationTimeSpan =
                config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerStrikeDecayTimeSpanConfigurationKey);

            // Build the list of strikes
            var strikesListString = string.Join("\n", strikes
                .OrderBy(s => s.ClubMember!.User!.Nickname)
                .ThenBy(s => s.Timestamp)
                .Select(s => $"- {s.ToStringDetailed(expirationTimeSpan)}"));

            // If the strikes list is too long
            if (strikesListString.Length > 1500)
            {
                // Convert string to stream
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
        public async Task ReadAllRelevantStrikesAsync()
        {
            try
            {
                // Defer the response
                await DeferAsync(ephemeral: true).ConfigureAwait(false);

                // Read the strikes
                var strikes = await readAllRelevantStrikesUseCase
                    .ReadAllRelevantStrikesAsync()
                    .ConfigureAwait(false);

                // If there are no relevant strikes
                if (strikes.Count == 0)
                {
                    // Respond
                    await FollowupAsync("There are currently no relevant strikes in the system.",
                            ephemeral: true)
                        .ConfigureAwait(false);
                    return;
                }

                // Build the list of strikes
                var strikesListString = string.Join("\n", strikes
                    .OrderByDescending(s => s.NumActiveStrikes)
                    .ThenBy(s => s.MemberNickname)
                    .Select(s => $"- {s}"));

                // If the strikes list is too long
                if (strikesListString.Length > 1500)
                {
                    // Convert string to stream
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
            }
            catch (Exception ex)
            {
                // Log error
                logger.LogError(ex, "Reading relevant strikes failed.");
                
                // Respond with error message
                await FollowupAsync("Reading relevant strikes failed: Internal error. Try again later. If the problem persists, please contact an admin.", ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("revoke", "Revoke a strike")]
        public async Task RevokeStrikeAsync(string strikeId)
        {
            // Parse the id
            var parseSuccessful = Guid.TryParse(strikeId, out var strikeIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Revoke the strike
            var revokeSuccessful = await revokeStrikeUseCase.RevokeStrikeAsync(strikeIdGuid).ConfigureAwait(false);

            // If the revoke was successful
            if (revokeSuccessful)
            {
                // Respond
                await RespondAsync($"Strike with id {strikeId} was successfully revoked.", ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                // Respond
                await RespondAsync($"Strike with id {strikeId} could not be revoked.", ephemeral: true).ConfigureAwait(false);
            }
        }

        [SlashCommand("unrevoke", "Remove a revocation of a strike")]
        public async Task UnrevokeStrikeAsync(string strikeId)
        {
            // Parse the id
            var parseSuccessful = Guid.TryParse(strikeId, out var strikeIdGuid);

            // If the parse was not successful
            if (!parseSuccessful)
            {
                // Respond
                await RespondAsync($"Invalid GUID '{strikeId}'. Please enter a valid GUID.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Revoke the strike
            var revokeSuccessful = await unrevokeStrikeUseCase.UnrevokeStrikeAsync(strikeIdGuid).ConfigureAwait(false);

            // If the revoke was successful
            if (revokeSuccessful)
            {
                // Respond
                await RespondAsync($"Revocation of strike with id {strikeId} was successfully removed.",
                    ephemeral: true).ConfigureAwait(false);
            }
            else
            {
                // Respond
                await RespondAsync($"Revocation of strike with id {strikeId} could not be removed.", ephemeral: true).ConfigureAwait(false);
            }
        }
    }
}