using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;
using Utilities;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// What a run would post on a day, without creating, storing or posting anything. Due results are shown
/// with the highscores as they stand right now. Works while the feature is disabled, so a file can be
/// checked before switching it on.
/// </summary>
/// <param name="Date">The day to preview.</param>
/// <param name="Weekday">The next occurrence of this weekday, counting today; used when no date is given.</param>
public sealed record PreviewCountryChallengesQuery(DateOnly? Date = null, DayOfWeek? Weekday = null)
    : IQuery<Result<CountryChallengePreview>>;

/// <param name="Title">What the message is: announcement, results or leaderboard.</param>
public sealed record CountryChallengePreviewMessage(string Title, ulong ChannelId, string Content);

/// <param name="Notes">What else the run would do or skip that day, in plain words.</param>
public sealed record CountryChallengePreview(
    DateOnly Date,
    bool FeatureEnabled,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<CountryChallengePreviewMessage> Messages);

public sealed class PreviewCountryChallengesHandler(
    ISender mediator,
    ICountryChallengeRepository repository,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<CountryChallengesConfiguration> options)
    : IRequestHandler<PreviewCountryChallengesQuery, Result<CountryChallengePreview>>
{
    private const int MinRounds = 5;

    public async Task<Result<CountryChallengePreview>> Handle(PreviewCountryChallengesQuery request, CancellationToken cancellationToken)
    {
        var planResult = await mediator.Send(new LoadCountryChallengePlanQuery(), cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return planResult.Error;
        }

        var plan = planResult.Value;
        var today = CountryChallengeCalendar.Today(DateTimeOffset.UtcNow, options.Value.ResolveTimeZone());
        var date = request.Date
                   ?? (request.Weekday is { } weekday ? CountryChallengeCalendar.NextOccurrence(today, weekday) : today);

        var notes = new List<string>();
        var messages = new List<CountryChallengePreviewMessage>();

        // Same order as a run: results, then the leaderboard (which already counts those results), then
        // the day's challenges.
        var pendingAwards = await PreviewResultsAsync(plan, date, notes, messages, cancellationToken).ConfigureAwait(false);
        await PreviewLeaderboardAsync(plan, date, pendingAwards, notes, messages, cancellationToken).ConfigureAwait(false);
        await PreviewAnnouncementAsync(plan, date, notes, messages, cancellationToken).ConfigureAwait(false);

        return new CountryChallengePreview(date, options.Value.Enabled, plan.Warnings, notes, messages);
    }

    private async Task<List<CountryChallengePointAward>> PreviewResultsAsync(
        CountryChallengePlan plan,
        DateOnly date,
        List<string> notes,
        List<CountryChallengePreviewMessage> messages,
        CancellationToken cancellationToken)
    {
        var due = await repository
            .ReadPostsDueForEvaluationAsync(date, date.AddDays(-EvaluateCountryChallengesHandler.GraceDays), cancellationToken)
            .ConfigureAwait(false);

        var pendingAwards = new List<CountryChallengePointAward>();
        if (due.Count == 0)
        {
            return pendingAwards;
        }

        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);
        var evaluated = new List<EvaluatedChallenge>();

        foreach (var post in due)
        {
            var label = $"{post.ChallengeName} of {post.Date:yyyy-MM-dd}";
            var results = plan.ResultsFor(post.ChallengeName);
            if (!results.Enabled)
            {
                notes.Add($"{label}: results are switched off, so it is closed without results.");
                continue;
            }

            List<ClubChallengeResultPlayer> players;
            try
            {
                var queryParams = new ReadHighscoresQueryParams { Limit = results.HighscoreLimit, MinRounds = MinRounds };
                var response = await client.ReadHighscoresAsync(post.ChallengeId, queryParams, cancellationToken).ConfigureAwait(false);
                players = ChallengeResultHighScoresAssembler.AssembleEntities(response);
            }
            catch (Exception ex)
            {
                notes.Add($"{label}: its highscores could not be read right now ({ex.Message}).");
                continue;
            }

            var points = CountryChallengeScoring.PointsByPlace(players.Count, results.Points);
            pendingAwards.AddRange(CountryChallengeScoring.Awards(plan.Leaderboard.Season, post, players, points, DateTimeOffset.UtcNow));
            evaluated.Add(new EvaluatedChallenge(post, results, players, points));

            var roles = results.RoleIds.Count > 0 ? $", and hands out {results.RoleIds.Count} role(s)" : string.Empty;
            notes.Add($"Evaluates {label} with the highscores as they are now{roles}.");
            if (!results.Post)
            {
                notes.Add($"{label}: its results are not posted (Results.Post is false); the points still count.");
            }
        }

        messages.AddRange(CountryChallengeMessages.Results(plan, date, evaluated)
            .Select(r => new CountryChallengePreviewMessage("Results", r.ChannelId, r.Message.Content)));

        return pendingAwards;
    }

    private async Task PreviewLeaderboardAsync(
        CountryChallengePlan plan,
        DateOnly date,
        List<CountryChallengePointAward> pendingAwards,
        List<string> notes,
        List<CountryChallengePreviewMessage> messages,
        CancellationToken cancellationToken)
    {
        var leaderboard = plan.Leaderboard;
        if (!leaderboard.IsDueOn(date))
        {
            return;
        }

        if (await repository.IsLeaderboardPostedOnAsync(date, cancellationToken).ConfigureAwait(false))
        {
            notes.Add("The leaderboard was already posted on this day.");
            return;
        }

        var awards = await repository.ReadAwardsAsync(leaderboard.Season, cancellationToken).ConfigureAwait(false);
        var standings = LeaderboardRanking.Rank(awards.Concat(pendingAwards));
        if (standings.Count == 0)
        {
            notes.Add("The leaderboard is due, but nobody has points yet, so it is not posted.");
            return;
        }

        var message = CountryChallengeMessages.Leaderboard(plan, date, standings);
        messages.Add(new CountryChallengePreviewMessage($"Leaderboard ({leaderboard.Season})", leaderboard.ChannelId, message.Content));
    }

    private async Task PreviewAnnouncementAsync(
        CountryChallengePlan plan,
        DateOnly date,
        List<string> notes,
        List<CountryChallengePreviewMessage> messages,
        CancellationToken cancellationToken)
    {
        var scheduled = plan.ChallengesOn(date);
        if (scheduled.Count == 0)
        {
            notes.Add($"No challenge is scheduled for {date.DayOfWeek} {date:yyyy-MM-dd}.");
            return;
        }

        var postedThatDay = (await repository.ReadPostsOnAsync(date, cancellationToken).ConfigureAwait(false))
            .GroupBy(p => p.ChallengeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Country).ToList(), StringComparer.OrdinalIgnoreCase);
        var items = new List<AnnouncementItem>();

        foreach (var challenge in scheduled)
        {
            var playedThatDay = postedThatDay.GetValueOrDefault(challenge.Name) ?? [];
            var missing = challenge.Picks - playedThatDay.Count;
            if (missing <= 0)
            {
                notes.Add($"{challenge.Name} was already posted on this day.");
                continue;
            }

            var history = await repository.ReadCountryHistoryAsync(challenge.Name, cancellationToken).ConfigureAwait(false);
            var countries = CountryPoolRotation.PickMany(challenge.Countries, history, missing, Random.Shared, playedThatDay);

            if (challenge.Countries.Count > missing)
            {
                var candidates = CountryPoolRotation.Candidates(challenge.Countries, history);
                var picks = missing == 1 ? "picks" : $"picks {missing}";
                notes.Add($"{challenge.Name} {picks} at random from {string.Join(", ", candidates.Select(c => c.Name))} " +
                          $"({candidates.Count} of {challenge.Countries.Count} left in this round); " +
                          $"previewed with {string.Join(" and ", countries.Select(c => c.Name))}.");
            }

            items.AddRange(countries.Select(country => new AnnouncementItem(challenge, country, CountryChallengeMessages.PreviewLink)));
        }

        foreach (var announcement in CountryChallengeMessages.Announcements(plan, date, items))
        {
            messages.Add(new CountryChallengePreviewMessage("Announcement", announcement.ChannelId, announcement.Message.Content));

            var mentions = announcement.Message.Mentions;
            var pings = mentions.RoleIds.Select(id => $"<@&{id}>")
                .Concat(mentions.UserIds.Select(id => $"<@{id}>"))
                .Concat(mentions.Everyone ? ["@everyone/@here"] : [])
                .ToList();
            notes.Add(pings.Count == 0 ? "The announcement pings no one." : $"The announcement pings {string.Join(", ", pings)}.");

            if (announcement.ThreadName is not null)
            {
                notes.Add($"Opens the thread \"{announcement.ThreadName}\" ({plan.Announcement.Thread.AutoArchive}).");
            }
        }
    }
}
