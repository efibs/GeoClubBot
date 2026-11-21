using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>Rates a batch of quiz pictures not rated yet, within the rating's share of the day (#491).</summary>
/// <param name="MaxPictures">Pictures to rate this run; null takes <c>AI:Quiz:Rating:MaxPicturesPerRun</c>.</param>
public sealed record RateQuizPicturesCommand(int? MaxPictures = null) : ICommand<Result<QuizRatingReport>>;

/// <param name="Attempted">Pictures a model was asked about.</param>
/// <param name="Rated">Pictures rated, whether or not they make a question.</param>
/// <param name="WithQuestions">Of those, the ones that make at least one question.</param>
/// <param name="Unusable">Replies that were no rating, left for a later run.</param>
/// <param name="Failed">Requests that failed outright.</param>
/// <param name="BudgetExhausted">The run stopped because rating has used its share of today's allowance.</param>
/// <param name="NoModel">No free model that reads pictures was available.</param>
public sealed record QuizRatingReport(
    int Attempted,
    int Rated,
    int WithQuestions,
    int Unusable,
    int Failed,
    bool BudgetExhausted,
    bool NoModel);

/// <summary>
/// Draws pictures without a current rating, a random country at a time (<see cref="QuizPictureDraw"/>), and
/// rates them. Every country gains questions from the first night on, not one large guide at a time.
/// </summary>
public sealed partial class RateQuizPicturesHandler(
    QuizPictureDraw draw,
    QuizPictureRater rater,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiQuizConfiguration> quizConfiguration,
    ILogger<RateQuizPicturesHandler> logger)
    : IRequestHandler<RateQuizPicturesCommand, Result<QuizRatingReport>>
{
    public const string DisabledCode = "ai.quiz_rating_disabled";

    /// <summary>Countries tried per picture wanted, so an index of mostly rated countries ends the search.</summary>
    private const int MaxCountriesPerPicture = 3;

    public async Task<Result<QuizRatingReport>> Handle(RateQuizPicturesCommand request, CancellationToken cancellationToken)
    {
        var quiz = quizConfiguration.Value;
        if (!aiConfiguration.Value.Active || !quiz.Enabled || !quiz.Rating.Enabled)
        {
            return Error.Validation(DisabledCode, "Rating quiz pictures is turned off (AI:Quiz:Rating:Enabled).");
        }

        var limit = Math.Max(1, request.MaxPictures ?? quiz.Rating.MaxPicturesPerRun);

        // Spread evenly over countries, however many pictures each country's guides hold.
        var unrated = await draw.DrawUnratedAsync(limit, countries: null, excludedSourceType: null, limit * MaxCountriesPerPicture, cancellationToken)
            .ConfigureAwait(false);

        int attempted = 0, rated = 0, withQuestions = 0, unusable = 0, failed = 0, misses = 0;
        bool budgetExhausted = false, noModel = false;

        var chain = unrated.Count == 0 ? [] : await rater.ReadChainAsync(cancellationToken).ConfigureAwait(false);
        if (unrated.Count > 0 && chain.Count == 0)
        {
            noModel = true;
        }

        foreach (var picture in chain.Count == 0 ? [] : unrated)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await rater.TryReserveAsync(quiz.Rating.MaxDailyBudgetPercent, cancellationToken).ConfigureAwait(false))
            {
                budgetExhausted = true;
                break;
            }

            attempted++;
            var outcome = await rater.RateAsync(picture, chain, cancellationToken).ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case QuizRatingOutcomeKind.Rated:
                    rated++;
                    withQuestions += outcome.Item!.Questions.Count > 0 ? 1 : 0;
                    misses = 0;
                    break;
                case QuizRatingOutcomeKind.Unusable:
                    unusable++;
                    misses++;
                    break;
                case QuizRatingOutcomeKind.RateLimited:
                    failed++;
                    break;
                case QuizRatingOutcomeKind.Failed:
                    failed++;
                    misses++;
                    chain = await rater.ReadChainAsync(cancellationToken).ConfigureAwait(false);
                    noModel = chain.Count == 0;
                    break;
            }

            if (outcome.Kind == QuizRatingOutcomeKind.RateLimited || noModel)
            {
                break;
            }

            if (misses >= Math.Max(1, quiz.Rating.MaxConsecutiveFailures))
            {
                LogStoppedForFailures(logger, misses);
                break;
            }
        }

        LogRun(logger, attempted, rated, withQuestions, unusable, failed);
        return new QuizRatingReport(attempted, rated, withQuestions, unusable, failed, budgetExhausted, noModel);
    }

    [LoggerMessage(LogLevel.Information,
        "Quiz rating run: {Attempted} asked, {Rated} rated ({WithQuestions} make a question), {Unusable} unusable, {Failed} failed.")]
    static partial void LogRun(ILogger logger, int attempted, int rated, int withQuestions, int unusable, int failed);

    [LoggerMessage(LogLevel.Warning, "Quiz rating run stopped: {Misses} unusable or failed replies in a row.")]
    static partial void LogStoppedForFailures(ILogger logger, int misses);
}
