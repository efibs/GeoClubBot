using Constants;
using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Users;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Imports the standings kept by hand before the bot tracked them, as points of the current season.
/// Every line must name exactly one player or nothing is imported, and an import replaces the previous
/// one of the season, so a corrected list can simply be imported again.
/// </summary>
public sealed record ImportCountryChallengeStandingsCommand(string Text)
    : ICommand<Result<CountryChallengeStandingsImport>>;

/// <param name="Top">The leaderboard after the import, as the posted one would show it.</param>
public sealed record CountryChallengeStandingsImport(
    string Season,
    int PlayerCount,
    int TotalPoints,
    IReadOnlyList<CountryChallengeStanding> Top);

public sealed class ImportCountryChallengeStandingsHandler(
    ISender mediator,
    IGeoGuessrUserRepository users,
    ICountryChallengeRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ImportCountryChallengeStandingsCommand, Result<CountryChallengeStandingsImport>>
{
    public const string InvalidImportCode = "CountryChallenges.InvalidImport";

    public async Task<Result<CountryChallengeStandingsImport>> Handle(
        ImportCountryChallengeStandingsCommand request,
        CancellationToken cancellationToken)
    {
        var planResult = await mediator.Send(new LoadCountryChallengePlanQuery(), cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return planResult.Error;
        }

        var leaderboard = planResult.Value.Leaderboard;
        var parse = StandingsImportParser.Parse(request.Text);
        var errors = parse.Errors.ToList();

        if (parse.Lines.Count == 0 && errors.Count == 0)
        {
            return Error.Validation(InvalidImportCode, "Nothing to import. Paste one player per line, like 'Fibs 12'.");
        }

        var players = new List<(string UserId, string Nickname, int Points)>(parse.Lines.Count);
        var lineOfPlayer = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in parse.Lines)
        {
            var user = await ResolveAsync(line, errors, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                continue;
            }

            if (lineOfPlayer.TryGetValue(user.UserId, out var firstLine))
            {
                errors.Add(new StandingsImportProblem(line.LineNumber, $"{user.Nickname} is already on line {firstLine}."));
                continue;
            }

            lineOfPlayer[user.UserId] = line.LineNumber;
            players.Add((user.UserId, user.Nickname, line.Points));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(
                InvalidImportCode,
                "Nothing was imported. Fix these lines and import the whole list again:\n" +
                string.Join("\n", errors.OrderBy(e => e.LineNumber).Select(e => $"• {e}")));
        }

        var now = DateTimeOffset.UtcNow;
        var awards = players
            .Where(p => p.Points > 0)
            .Select(p => CountryChallengePointAward.Imported(
                leaderboard.Season,
                p.UserId,
                CountryChallengeScoring.Truncate(p.Nickname, StringLengthConstants.GeoGuessrPlayerNicknameMaxLength),
                p.Points,
                now))
            .ToList();

        await repository.ReplaceImportedAwardsAsync(leaderboard.Season, awards, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var standings = LeaderboardRanking.Rank(
            await repository.ReadAwardsAsync(leaderboard.Season, cancellationToken).ConfigureAwait(false));

        return new CountryChallengeStandingsImport(
            leaderboard.Season,
            players.Count,
            players.Sum(p => p.Points),
            [.. standings.Where(s => s.Rank <= leaderboard.Top)]);
    }

    private async Task<GeoGuessrUser?> ResolveAsync(
        StandingsImportLine line,
        List<StandingsImportProblem> errors,
        CancellationToken cancellationToken)
    {
        if (line.UserId is not null)
        {
            // Also syncs a player the bot has never seen, which is the point of accepting profile links.
            var result = await mediator
                .Send(new ReadOrSyncGeoGuessrUserByUserIdQuery(line.UserId), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                errors.Add(new StandingsImportProblem(line.LineNumber, $"no GeoGuessr player has the id {line.UserId}."));
                return null;
            }

            return result.Value;
        }

        var matches = await users.ReadUsersByNicknameAsync(line.Player, cancellationToken).ConfigureAwait(false);
        switch (matches.Count)
        {
            case 1:
                return matches[0];
            case 0:
                errors.Add(new StandingsImportProblem(line.LineNumber,
                    $"'{line.Player}' is not a GeoGuessr player the bot knows. Use their profile link (geoguessr.com/user/…) instead."));
                return null;
            default:
                errors.Add(new StandingsImportProblem(line.LineNumber,
                    $"several players are called '{line.Player}'. Use the profile link of the right one: " +
                    string.Join(", ", matches.Select(m => $"https://www.geoguessr.com/user/{m.UserId}"))));
                return null;
        }
    }
}
