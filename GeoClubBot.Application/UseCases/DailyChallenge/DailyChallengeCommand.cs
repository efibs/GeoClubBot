using System.Text;
using System.Text.Json;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.DailyChallenge;

public sealed record DailyChallengeCommand : ICommand;

/// <summary>
/// Runs the daily challenge in three phases: reward the players of the challenges that just ended,
/// create the challenges for the next round, and publish both to Discord. The phases are isolated
/// from each other, so a GeoGuessr or Discord hiccup in one of them cannot take the others down:
/// publishing always runs, and when neither of the first two phases delivered anything it says so.
/// </summary>
public sealed partial class DailyChallengeHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IClubChallengeRepository clubChallenges,
    IDiscordMessageAccess discordMessageAccess,
    ISender mediator,
    IUnitOfWork unitOfWork,
    ILogger<DailyChallengeHandler> logger,
    IOptions<DailyChallengesConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<DailyChallengeCommand, Unit>
{
    private const string BothPhasesFailedMessage =
        "# :warning: The daily challenge could not be run\n" +
        "Neither the results of the last challenges nor new challenges could be fetched. " +
        "The challenges that are already running stay active — check the bot logs for the cause.";

    private const string NoResultsMessage =
        "# :warning: The results of the last challenges could not be determined.";

    private const string NoChallengesMessage =
        "# :warning: No new challenges could be created. The current ones stay active.";

    private static readonly Random Rng = new();

    // Challenges are always created on behalf of the main club's account.
    private IGeoGuessrClient GeoGuessrClient =>
        _geoGuessrClient ??= geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);
    private IGeoGuessrClient? _geoGuessrClient;

    public async Task<Unit> Handle(DailyChallengeCommand request, CancellationToken cancellationToken)
    {
        // Reading the active challenges is the one step without a fallback: a database that cannot
        // be read has nothing to reward, nothing to replace and nowhere to store the next round.
        var activeLinks = await clubChallenges.ReadLatestClubChallengeLinksAsync(cancellationToken).ConfigureAwait(false);

        var previous = await RewardPreviousChallengesAsync(activeLinks, cancellationToken).ConfigureAwait(false);
        var next = await CreateNextChallengesAsync(activeLinks, cancellationToken).ConfigureAwait(false);

        await PublishAsync(previous, next, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    /// <summary>The outcome of a phase: what it produced, and whether it produced all of it.</summary>
    private sealed record PreviousChallenges(List<ClubChallengeResult> Results, bool Faulted);

    private sealed record NextChallenges(List<ClubChallenge> Challenges, bool Faulted);

    /// <summary>
    /// Phase 1: read the leaderboards of the challenges that just ended and hand out the podium
    /// roles. A leaderboard that cannot be read only costs that one difficulty its rewards.
    /// </summary>
    private async Task<PreviousChallenges> RewardPreviousChallengesAsync(
        List<ClubChallengeLink> activeLinks,
        CancellationToken cancellationToken)
    {
        var results = new List<ClubChallengeResult>(activeLinks.Count);
        var faulted = false;

        foreach (var link in activeLinks)
        {
            try
            {
                var queryParams = new ReadHighscoresQueryParams { Limit = 10, MinRounds = 5 };
                var response = await GeoGuessrClient
                    .ReadHighscoresAsync(link.ChallengeId, queryParams, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new ClubChallengeResult(
                    link.Difficulty,
                    link.RolePriority,
                    ChallengeResultHighScoresAssembler.AssembleEntities(response)));
            }
            catch (Exception ex)
            {
                LogHighscoresFailed(logger, ex, link.Difficulty, link.ChallengeId);
                faulted = true;
            }
        }

        try
        {
            await mediator
                .Send(new DistributeDailyChallengeRolesCommand(results), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The roles are a reward, not the announcement: the players still get to read the
            // results in Discord even when Discord refused the role update.
            LogRoleDistributionFailed(logger, ex);
            faulted = true;
        }

        return new PreviousChallenges(results, faulted);
    }

    /// <summary>
    /// Phase 2: create one challenge per configured difficulty and make it the active one. A
    /// difficulty whose challenge could not be created keeps the challenge it already has, so its
    /// players are rewarded on the next run instead of being dropped.
    /// </summary>
    private async Task<NextChallenges> CreateNextChallengesAsync(
        List<ClubChallengeLink> activeLinks,
        CancellationToken cancellationToken)
    {
        List<ClubChallengeConfigurationDifficulty> challengeConfig;
        try
        {
            challengeConfig = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogConfigurationUnreadable(logger, ex, config.Value.ConfigurationFilePath);
            return new NextChallenges([], Faulted: true);
        }

        var challenges = new List<ClubChallenge>(challengeConfig.Count);
        var newLinks = new List<ClubChallengeLink>(challengeConfig.Count);
        var keptDifficulties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var difficulty in challengeConfig)
        {
            var (challenge, link) = await CreateChallengeAsync(difficulty, cancellationToken).ConfigureAwait(false);
            challenges.Add(challenge);

            if (link is null)
            {
                keptDifficulties.Add(difficulty.Difficulty);
            }
            else
            {
                newLinks.Add(link);
            }
        }

        if (newLinks.Count == 0)
        {
            // Nothing to swap in — leave the active challenges alone so the next run can retry them.
            return new NextChallenges(challenges, Faulted: true);
        }

        clubChallenges.AddLatestClubChallengeLinks(newLinks);
        clubChallenges.DeleteLatestClubChallengeLinks(
            activeLinks.Where(l => !keptDifficulties.Contains(l.Difficulty)));

        try
        {
            // Commit before announcing: a challenge the players are told about but that was never
            // stored is played without anyone ever being rewarded for it.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogChallengesNotStored(logger, ex);
            return new NextChallenges(challenges, Faulted: true);
        }

        return new NextChallenges(challenges, Faulted: false);
    }

    /// <summary>
    /// Phase 3: always runs. Publishes whatever the phases above produced, and replaces the part
    /// they could not deliver with a notice — or a single failure notice if that is both of them.
    /// </summary>
    private async Task PublishAsync(
        PreviousChallenges previous,
        NextChallenges next,
        CancellationToken cancellationToken)
    {
        if (previous.Faulted && next.Faulted)
        {
            await TryPublishAsync(() => SendMessageAsync(BothPhasesFailedMessage, cancellationToken))
                .ConfigureAwait(false);
            return;
        }

        // Two separate attempts: failing to publish the results must not cost the players the
        // announcement of the challenges they are supposed to play today.
        await TryPublishAsync(async () =>
        {
            if (previous.Results.Count > 0)
            {
                await SendLastChallengeResultsAsync(previous.Results, cancellationToken).ConfigureAwait(false);
            }
            else if (previous.Faulted)
            {
                await SendMessageAsync(NoResultsMessage, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        await TryPublishAsync(async () =>
        {
            // Only challenges that are stored may be announced; a faulted phase 2 stored none, so
            // the players are told that the challenges they already have stay active instead.
            if (next.Faulted)
            {
                await SendMessageAsync(NoChallengesMessage, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SendNextChallengesAsync(next.Challenges, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private async Task TryPublishAsync(Func<Task> publish)
    {
        try
        {
            await publish().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPublishFailed(logger, ex);
        }
    }

    private async Task<List<ClubChallengeConfigurationDifficulty>> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        var configFileString = await File
            .ReadAllTextAsync(config.Value.ConfigurationFilePath, cancellationToken)
            .ConfigureAwait(false);

        return JsonSerializer.Deserialize<List<ClubChallengeConfigurationDifficulty>>(configFileString)
               ?? throw new InvalidOperationException(
                   $"Invalid challenge configuration file: {config.Value.ConfigurationFilePath}");
    }

    /// <summary>
    /// Creates the challenge for one difficulty. Returns a challenge without a link when GeoGuessr
    /// could not create it, which publishes as an error entry and keeps the current challenge alive.
    /// </summary>
    private async Task<(ClubChallenge Challenge, ClubChallengeLink? Link)> CreateChallengeAsync(
        ClubChallengeConfigurationDifficulty difficulty,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = difficulty.Entries[Rng.Next(0, difficulty.Entries.Count)];
            var apiRequest = new PostChallengeRequestDto
            {
                AccessLevel = 1,
                ChallengeType = 0,
                ForbidMoving = entry.ForbidMoving,
                ForbidRotating = entry.ForbidRotating,
                ForbidZooming = entry.ForbidZooming,
                Map = entry.MapId,
                TimeLimit = entry.TimeLimit
            };

            var response = await GeoGuessrClient.CreateChallengeAsync(apiRequest, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response.Token))
            {
                // A challenge without a token cannot be played or linked; treat it like a failure.
                throw new InvalidOperationException("GeoGuessr returned a challenge without a token.");
            }

            return (
                new ClubChallenge(difficulty.Difficulty, difficulty.RolePriority, entry.Description, response.Token),
                ClubChallengeLink.Create(difficulty.Difficulty, difficulty.RolePriority, response.Token));
        }
        catch (Exception ex)
        {
            LogChallengeCreationFailed(logger, ex, difficulty.Difficulty);
            return (new ClubChallenge(difficulty.Difficulty, difficulty.RolePriority, string.Empty, string.Empty), null);
        }
    }

    private Task SendMessageAsync(string message, CancellationToken cancellationToken) =>
        discordMessageAccess.SendMessageAsync(message, config.Value.TextChannelId, cancellationToken);

    private async Task SendLastChallengeResultsAsync(List<ClubChallengeResult> lastChallengeResults, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("# :trophy: The results are in! :trophy: ");

        foreach (var lastChallengeResult in lastChallengeResults)
        {
            builder.Append("\n## ");
            builder.Append(lastChallengeResult.Difficulty);

            if (!lastChallengeResult.Players.Any())
            {
                builder.AppendLine();
                builder.Append("No one participated :frowning2: ");
            }
            else
            {
                AppendPlayers(builder, lastChallengeResult.Players);
            }

            await SendMessageAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);

            builder = new StringBuilder();
        }
    }

    private static void AppendPlayers(StringBuilder builder, List<ClubChallengeResultPlayer> players)
    {
        var place = 1;
        foreach (var player in players)
        {
            builder.AppendLine();
            switch (place)
            {
                case 1: builder.Append(":first_place:"); break;
                case 2: builder.Append(":second_place:"); break;
                case 3: builder.Append(":third_place:"); break;
                default:
                    builder.Append(place);
                    builder.Append(". ");
                    break;
            }

            builder.Append(player.Nickname);
            builder.Append(" (");
            builder.Append(player.TotalScore);
            builder.Append(", ");
            builder.Append(player.TotalDistance);
            builder.Append(')');

            place++;
        }
    }

    private async Task SendNextChallengesAsync(List<ClubChallenge> nextChallenges, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder("# :dart: Next challenges :dart:");

        foreach (var nextChallenge in nextChallenges)
        {
            if (nextChallenge.ChallengeId == string.Empty)
            {
                builder.Append("\n - ");
                builder.Append(nextChallenge.Difficulty);
                builder.Append(": ERROR");
                continue;
            }

            builder.Append("\n - [");
            builder.Append(nextChallenge.Difficulty);
            builder.Append(" (");
            builder.Append(nextChallenge.Description);
            builder.Append(")](https://www.geoguessr.com/challenge/");
            builder.Append(nextChallenge.ChallengeId);
            builder.Append(")");
        }

        await SendMessageAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(LogLevel.Error, "Could not read the highscores of the '{difficulty}' challenge '{challengeId}'.")]
    static partial void LogHighscoresFailed(
        ILogger<DailyChallengeHandler> logger,
        Exception exception,
        string difficulty,
        string challengeId);

    [LoggerMessage(LogLevel.Error, "Could not distribute the daily challenge roles.")]
    static partial void LogRoleDistributionFailed(ILogger<DailyChallengeHandler> logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Could not read the challenge configuration file '{configurationFilePath}'.")]
    static partial void LogConfigurationUnreadable(
        ILogger<DailyChallengeHandler> logger,
        Exception exception,
        string configurationFilePath);

    [LoggerMessage(LogLevel.Error, "Could not create the next '{difficulty}' challenge.")]
    static partial void LogChallengeCreationFailed(
        ILogger<DailyChallengeHandler> logger,
        Exception exception,
        string difficulty);

    [LoggerMessage(LogLevel.Error, "Could not store the new daily challenges; they will not be announced.")]
    static partial void LogChallengesNotStored(ILogger<DailyChallengeHandler> logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Could not publish the daily challenge message.")]
    static partial void LogPublishFailed(ILogger<DailyChallengeHandler> logger, Exception exception);
}
