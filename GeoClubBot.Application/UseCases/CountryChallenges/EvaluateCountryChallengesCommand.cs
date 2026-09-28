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
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Evaluates every country challenge whose results are due: reads its highscores, awards the points,
/// posts the results and hands out the roles.
/// </summary>
/// <param name="IncludeNotYetDue">
/// Evaluate every challenge still waiting for its results, not only those due by <paramref name="Date"/>
/// — an admin settling them early.
/// </param>
public sealed record EvaluateCountryChallengesCommand(CountryChallengePlan Plan, DateOnly Date, bool IncludeNotYetDue = false)
    : ICommand<CountryChallengeEvaluationOutcome>;

public sealed partial class EvaluateCountryChallengesHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ICountryChallengeRepository repository,
    IDiscordMessageAccess discordMessageAccess,
    ISender mediator,
    IUnitOfWork unitOfWork,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<EvaluateCountryChallengesHandler> logger)
    : IRequestHandler<EvaluateCountryChallengesCommand, CountryChallengeEvaluationOutcome>
{
    /// <summary>
    /// How long a challenge whose highscores cannot be read is retried. Long enough to outlast a GeoGuessr
    /// outage, short enough that a challenge GeoGuessr has deleted is not retried forever.
    /// </summary>
    public const int GraceDays = 7;

    /// <summary>Only players who finished every round are ranked, as in the daily challenge.</summary>
    private const int MinRounds = 5;

    public async Task<CountryChallengeEvaluationOutcome> Handle(
        EvaluateCountryChallengesCommand request,
        CancellationToken cancellationToken)
    {
        var plan = request.Plan;

        var due = request.IncludeNotYetDue
            ? await repository.ReadPendingPostsAsync(cancellationToken).ConfigureAwait(false)
            : await repository
                .ReadPostsDueForEvaluationAsync(request.Date, request.Date.AddDays(-GraceDays), cancellationToken)
                .ConfigureAwait(false);

        if (due.Count == 0)
        {
            return CountryChallengeEvaluationOutcome.Nothing;
        }

        // Challenges are created on behalf of the main club's account, so their highscores are read with it too.
        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);
        var now = DateTimeOffset.UtcNow;
        var evaluated = new List<EvaluatedChallenge>(due.Count);
        var failed = new List<string>();

        foreach (var post in due)
        {
            var results = plan.ResultsFor(post.ChallengeName);
            if (!results.Enabled)
            {
                // Results were switched off after the challenge was posted; there is nothing left to do.
                post.MarkEvaluated(now);
                continue;
            }

            List<ClubChallengeResultPlayer> players;
            try
            {
                var queryParams = new ReadHighscoresQueryParams { Limit = results.HighscoreLimit, MinRounds = MinRounds };
                var response = await client
                    .ReadHighscoresAsync(post.ChallengeId, queryParams, cancellationToken)
                    .ConfigureAwait(false);
                players = ChallengeResultHighScoresAssembler.AssembleEntities(response);
            }
            catch (Exception ex)
            {
                // Left pending, so the next run tries again.
                LogHighscoresFailed(logger, ex, post.ChallengeName, post.Date, post.ChallengeId);
                failed.Add(Label(post));
                continue;
            }

            var points = CountryChallengeScoring.PointsByPlace(players.Count, results.Points);
            repository.AddAwards(CountryChallengeScoring.Awards(plan.Leaderboard.Season, post, players, points, now));
            post.MarkEvaluated(now);
            evaluated.Add(new EvaluatedChallenge(post, results, players, points));
        }

        try
        {
            // Stored before anything is posted: points that were announced must also have been counted.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogEvaluationNotStored(logger, ex);
            return new CountryChallengeEvaluationOutcome([], [.. due.Select(Label)]);
        }

        await PublishResultsAsync(plan, request.Date, evaluated, cancellationToken).ConfigureAwait(false);
        await DistributeRolesAsync(evaluated, cancellationToken).ConfigureAwait(false);

        return new CountryChallengeEvaluationOutcome([.. evaluated.Select(e => Label(e.Post))], failed);
    }

    private async Task PublishResultsAsync(
        CountryChallengePlan plan,
        DateOnly date,
        List<EvaluatedChallenge> evaluated,
        CancellationToken cancellationToken)
    {
        foreach (var results in CountryChallengeMessages.Results(plan, date, evaluated))
        {
            try
            {
                await discordMessageAccess
                    .SendMessageAsync(results.Message.Content, results.ChannelId, results.Message.Mentions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The points are stored either way; they show up on the next leaderboard.
                LogResultsNotPublished(logger, ex, results.ChannelId);
            }
        }
    }

    private async Task DistributeRolesAsync(List<EvaluatedChallenge> evaluated, CancellationToken cancellationToken)
    {
        var assignments = CountryChallengeRoleAllocation.ForResults(
            evaluated.Select(e => (e.Results.RoleIds, (IReadOnlyList<string>)[.. e.Players.Select(p => p.UserId)])));

        foreach (var assignment in assignments)
        {
            try
            {
                await mediator
                    .Send(new DistributeCountryChallengeRolesCommand(assignment), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRolesNotDistributed(logger, ex);
            }
        }
    }

    private static string Label(CountryChallengePost post) => $"{post.ChallengeName} ({post.Date:yyyy-MM-dd})";

    [LoggerMessage(LogLevel.Error, "Could not read the highscores of the country challenge '{challengeName}' of {date} ('{challengeId}').")]
    static partial void LogHighscoresFailed(
        ILogger<EvaluateCountryChallengesHandler> logger,
        Exception exception,
        string challengeName,
        DateOnly date,
        string challengeId);

    [LoggerMessage(LogLevel.Error, "Could not store the evaluated country challenges; they will be evaluated again at the next run.")]
    static partial void LogEvaluationNotStored(ILogger<EvaluateCountryChallengesHandler> logger, Exception exception);

    [LoggerMessage(LogLevel.Error, "Could not post the country challenge results to channel {channelId}.")]
    static partial void LogResultsNotPublished(ILogger<EvaluateCountryChallengesHandler> logger, Exception exception, ulong channelId);

    [LoggerMessage(LogLevel.Error, "Could not hand out the country challenge roles.")]
    static partial void LogRolesNotDistributed(ILogger<EvaluateCountryChallengesHandler> logger, Exception exception);
}
