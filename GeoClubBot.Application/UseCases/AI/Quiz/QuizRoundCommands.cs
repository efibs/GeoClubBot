using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>Remembers the message a round was posted as, so its answer can be shown there later.</summary>
public sealed record AttachQuizMessageCommand(Guid RoundId, ulong MessageId) : ICommand<Result>;

public sealed class AttachQuizMessageHandler(IAiQuizRoundRepository rounds)
    : IRequestHandler<AttachQuizMessageCommand, Result>
{
    public async Task<Result> Handle(AttachQuizMessageCommand request, CancellationToken cancellationToken)
    {
        var round = await rounds.ReadAsync(request.RoundId, cancellationToken).ConfigureAwait(false);
        if (round is null)
        {
            return Error.NotFound(QuizErrorCodes.RoundNotFound, "That quiz question is gone.");
        }

        round.AttachMessage(request.MessageId);
        return Result.Success();
    }
}

/// <param name="Round">The round, with the guess among its guesses.</param>
/// <param name="Guess">The member's guess and what it scored.</param>
/// <param name="AllTime">The member's record over all time, this guess included.</param>
/// <param name="Recent">The member's record over the leaderboard's recent window, this guess included.</param>
public sealed record QuizGuessOutcome(AiQuizRound Round, AiQuizGuess Guess, QuizScoreLine AllTime, QuizScoreLine Recent, int RecentDays);

/// <summary>A member's one guess at a quiz question (#491).</summary>
public sealed record GuessQuizCommand(Guid RoundId, ulong UserId, int OptionIndex) : ICommand<Result<QuizGuessOutcome>>;

public sealed class GuessQuizHandler(
    IAiQuizRoundRepository rounds,
    IOptions<AiQuizConfiguration> configuration,
    TimeProvider timeProvider)
    : IRequestHandler<GuessQuizCommand, Result<QuizGuessOutcome>>
{
    public async Task<Result<QuizGuessOutcome>> Handle(GuessQuizCommand request, CancellationToken cancellationToken)
    {
        var round = await rounds.ReadAsync(request.RoundId, cancellationToken).ConfigureAwait(false);
        if (round is null)
        {
            return Error.NotFound(QuizErrorCodes.RoundNotFound, "That quiz question is gone; draw a new one.");
        }

        if (round.GuessOf(request.UserId) is { } earlier)
        {
            return Error.Conflict(QuizErrorCodes.AlreadyGuessed,
                $"You already guessed **{round.Options[earlier.OptionIndex]}** — one guess each.");
        }

        if (round.IsRevealed)
        {
            return Error.Conflict(QuizErrorCodes.AlreadyRevealed, "The answer is out already; draw a new one.");
        }

        var now = timeProvider.GetUtcNow();
        var guess = round.Guess(request.UserId, request.OptionIndex, now);
        if (guess is null)
        {
            return Error.Validation(QuizErrorCodes.RoundNotFound, "That is none of the answers.");
        }

        // Read before this guess is saved, so it is added on top.
        var days = Math.Max(1, configuration.Value.LeaderboardRecentDays);
        var allTime = await ReadAsync(null, request.UserId, guess, cancellationToken).ConfigureAwait(false);
        var recent = await ReadAsync(now.AddDays(-days), request.UserId, guess, cancellationToken).ConfigureAwait(false);

        return new QuizGuessOutcome(round, guess, allTime, recent, days);
    }

    private async Task<QuizScoreLine> ReadAsync(DateTimeOffset? since, ulong userId, AiQuizGuess guess, CancellationToken cancellationToken)
    {
        var scores = await rounds.ReadScoresAsync(since, cancellationToken).ConfigureAwait(false);
        var line = scores.FirstOrDefault(score => score.UserId == userId) ?? new QuizScoreLine(userId, 0, 0, 0);
        return line with
        {
            Points = line.Points + guess.Points,
            Right = line.Right + (guess.Correct ? 1 : 0),
            Answered = line.Answered + 1
        };
    }
}

/// <summary>Shows a round's answer to everyone at the asker's or an admin's request (#491).</summary>
/// <param name="IsAdmin">Whether the member may reveal any question.</param>
public sealed record RevealQuizRoundCommand(Guid RoundId, ulong UserId, bool IsAdmin) : ICommand<Result<AiQuizRound>>;

public sealed class RevealQuizRoundHandler(IAiQuizRoundRepository rounds, TimeProvider timeProvider)
    : IRequestHandler<RevealQuizRoundCommand, Result<AiQuizRound>>
{
    public async Task<Result<AiQuizRound>> Handle(RevealQuizRoundCommand request, CancellationToken cancellationToken)
    {
        var round = await rounds.ReadAsync(request.RoundId, cancellationToken).ConfigureAwait(false);
        if (round is null)
        {
            return Error.NotFound(QuizErrorCodes.RoundNotFound, "That quiz question is gone.");
        }

        if (!request.IsAdmin && round.AskedByUserId != request.UserId)
        {
            return Error.Validation(QuizErrorCodes.NotYoursToReveal,
                "Only whoever started this question, or an admin, can reveal it early. It reveals itself soon.");
        }

        round.Reveal(timeProvider.GetUtcNow());
        return round;
    }
}

/// <summary>Shows the answer of every round whose time is up (#491); run every minute.</summary>
public sealed record RevealDueQuizRoundsCommand : ICommand<Result<int>>;

public sealed partial class RevealDueQuizRoundsHandler(
    IAiQuizRoundRepository rounds,
    IAiQuizMessages messages,
    TimeProvider timeProvider,
    ILogger<RevealDueQuizRoundsHandler> logger)
    : IRequestHandler<RevealDueQuizRoundsCommand, Result<int>>
{
    /// <summary>Rounds revealed per run; more wait a minute.</summary>
    private const int MaxPerRun = 20;

    public async Task<Result<int>> Handle(RevealDueQuizRoundsCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await rounds.ReadDueAsync(now, MaxPerRun, cancellationToken).ConfigureAwait(false);

        foreach (var round in due)
        {
            round.Reveal(now);

            // A round whose post failed has no message to show it in, and one whose message is gone
            // cannot be shown either: each is marked revealed all the same, so it is not tried every minute.
            if (round.MessageId is not null && !await messages.ShowAnswerAsync(round, cancellationToken).ConfigureAwait(false))
            {
                LogNotShown(logger, round.Id, round.ChannelId);
            }
        }

        return due.Count;
    }

    [LoggerMessage(LogLevel.Warning, "Quiz: the answer of round {RoundId} could not be shown in channel {ChannelId}.")]
    static partial void LogNotShown(ILogger logger, Guid roundId, ulong channelId);
}

/// <param name="AllTime">The best members over all time.</param>
/// <param name="Recent">The best members over the last <see cref="RecentDays"/> days.</param>
/// <param name="Player">The asking member's own record and rank over all time; null when they never answered.</param>
/// <param name="PlayerRecent">The same over the recent window.</param>
public sealed record QuizLeaderboard(
    IReadOnlyList<QuizScoreLine> AllTime,
    IReadOnlyList<QuizScoreLine> Recent,
    int RecentDays,
    (QuizScoreLine Line, int Rank)? Player,
    (QuizScoreLine Line, int Rank)? PlayerRecent);

/// <summary>The quiz's leaderboard: points over all time and over a recent window (#491).</summary>
public sealed record ReadQuizLeaderboardQuery(ulong UserId) : IQuery<Result<QuizLeaderboard>>;

public sealed class ReadQuizLeaderboardHandler(
    IAiQuizRoundRepository rounds,
    IOptions<AiQuizConfiguration> configuration,
    TimeProvider timeProvider)
    : IRequestHandler<ReadQuizLeaderboardQuery, Result<QuizLeaderboard>>
{
    public async Task<Result<QuizLeaderboard>> Handle(ReadQuizLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var settings = configuration.Value;
        var days = Math.Max(1, settings.LeaderboardRecentDays);
        var size = Math.Max(1, settings.LeaderboardSize);

        var allTime = await rounds.ReadScoresAsync(null, cancellationToken).ConfigureAwait(false);
        var recent = await rounds.ReadScoresAsync(timeProvider.GetUtcNow().AddDays(-days), cancellationToken).ConfigureAwait(false);

        return new QuizLeaderboard(
            [.. allTime.Take(size)],
            [.. recent.Take(size)],
            days,
            RankOf(allTime, request.UserId),
            RankOf(recent, request.UserId));
    }

    /// <summary>
    /// A member's line and rank, ties sharing a rank: the rank is one more than the members with more points.
    /// </summary>
    public static (QuizScoreLine Line, int Rank)? RankOf(IReadOnlyList<QuizScoreLine> scores, ulong userId)
    {
        var line = scores.FirstOrDefault(score => score.UserId == userId);
        return line is null ? null : (line, scores.Count(score => score.Points > line.Points) + 1);
    }
}
