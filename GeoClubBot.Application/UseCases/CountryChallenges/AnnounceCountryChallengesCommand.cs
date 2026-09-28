using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>
/// Creates the challenges scheduled for the date that have not been posted yet, and announces them in
/// one message per channel.
/// </summary>
public sealed record AnnounceCountryChallengesCommand(CountryChallengePlan Plan, DateOnly Date)
    : ICommand<CountryChallengeAnnouncementOutcome>;

public sealed partial class AnnounceCountryChallengesHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ICountryChallengeRepository repository,
    IDiscordMessageAccess discordMessageAccess,
    IUnitOfWork unitOfWork,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<AnnounceCountryChallengesHandler> logger)
    : IRequestHandler<AnnounceCountryChallengesCommand, CountryChallengeAnnouncementOutcome>
{
    public async Task<CountryChallengeAnnouncementOutcome> Handle(
        AnnounceCountryChallengesCommand request,
        CancellationToken cancellationToken)
    {
        var plan = request.Plan;
        var date = request.Date;

        var scheduled = plan.ChallengesOn(date);
        if (scheduled.Count == 0)
        {
            return CountryChallengeAnnouncementOutcome.Nothing;
        }

        // A challenge with several picks may have been partly posted by an earlier run that day; only the
        // countries still missing are created, and never one it already played.
        var postedToday = (await repository.ReadPostsOnAsync(date, cancellationToken).ConfigureAwait(false))
            .GroupBy(p => p.ChallengeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Country).ToList(), StringComparer.OrdinalIgnoreCase);

        var alreadyPosted = new List<string>();
        var due = new List<(ChallengePlan Challenge, int Missing, List<string> PlayedToday)>();
        foreach (var challenge in scheduled)
        {
            var playedToday = postedToday.GetValueOrDefault(challenge.Name) ?? [];
            var missing = challenge.Picks - playedToday.Count;

            if (missing > 0)
            {
                due.Add((challenge, missing, playedToday));
            }
            else
            {
                alreadyPosted.Add(challenge.Name);
            }
        }

        if (due.Count == 0)
        {
            return new CountryChallengeAnnouncementOutcome([], alreadyPosted, []);
        }

        var (items, posts, failed) = await CreateChallengesAsync(due, date, cancellationToken).ConfigureAwait(false);

        if (posts.Count > 0)
        {
            repository.AddPosts(posts.Values);

            try
            {
                // Stored before announcing: a challenge the players are told about but that was never stored
                // is played without anyone ever being rewarded for it.
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogNotStored(logger, ex, date);
                repository.RemovePosts(posts.Values);
                return new CountryChallengeAnnouncementOutcome([], alreadyPosted, [.. items.Select(Label)]);
            }
        }

        var announced = new List<string>();
        foreach (var announcement in CountryChallengeMessages.Announcements(plan, date, items))
        {
            var created = announcement.Items.Where(i => i.Link is not null).ToList();

            if (await TryAnnounceAsync(plan, announcement, cancellationToken).ConfigureAwait(false))
            {
                announced.AddRange(created.Select(Label));
                continue;
            }

            failed.AddRange(created.Select(Label));
            await ForgetAsync([.. created.Select(item => posts[item])], cancellationToken).ConfigureAwait(false);
        }

        return new CountryChallengeAnnouncementOutcome(announced, alreadyPosted, failed);
    }

    private static string Label(AnnouncementItem item) => $"{item.Challenge.Name}: {item.Country.Name}";

    /// <summary>
    /// Creates the missing challenges on GeoGuessr. Returns every item to announce — a failed one without a
    /// link — and the post of each created one, keyed by its item.
    /// </summary>
    private async Task<(List<AnnouncementItem> Items, Dictionary<AnnouncementItem, CountryChallengePost> Posts, List<string> Failed)>
        CreateChallengesAsync(
            List<(ChallengePlan Challenge, int Missing, List<string> PlayedToday)> due,
            DateOnly date,
            CancellationToken cancellationToken)
    {
        // Challenges are always created on behalf of the main club's account, like the daily challenge.
        var client = geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);
        var now = DateTimeOffset.UtcNow;

        var items = new List<AnnouncementItem>();
        var posts = new Dictionary<AnnouncementItem, CountryChallengePost>(ReferenceEqualityComparer.Instance);
        var failed = new List<string>();

        foreach (var (challenge, missing, playedToday) in due)
        {
            var history = await repository.ReadCountryHistoryAsync(challenge.Name, cancellationToken).ConfigureAwait(false);
            var countries = CountryPoolRotation.PickMany(challenge.Countries, history, missing, Random.Shared, playedToday);

            foreach (var country in countries)
            {
                string challengeId;
                try
                {
                    challengeId = await CreateChallengeAsync(client, country, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Announced as a warning line; a later run the same day tries again.
                    LogCreationFailed(logger, ex, challenge.Name, country.Name);
                    var failedItem = new AnnouncementItem(challenge, country, null);
                    failed.Add(Label(failedItem));
                    items.Add(failedItem);
                    continue;
                }

                var item = new AnnouncementItem(challenge, country, CountryChallengeMessages.ChallengeLink(challengeId));
                var settings = country.Settings;
                posts[item] = CountryChallengePost.Create(
                    challenge.Name,
                    date,
                    country.Name,
                    country.Code,
                    country.MapId,
                    country.MapName,
                    settings.TimeLimit,
                    settings.ForbidMoving,
                    settings.ForbidRotating,
                    settings.ForbidZooming,
                    challengeId,
                    challenge.ChannelId,
                    now,
                    challenge.Results.Enabled ? date.AddDays(challenge.Results.AfterDays) : null);

                items.Add(item);
            }
        }

        return (items, posts, failed);
    }

    private static async Task<string> CreateChallengeAsync(
        IGeoGuessrClient client,
        CountryPlan country,
        CancellationToken cancellationToken)
    {
        var request = new PostChallengeRequestDto
        {
            AccessLevel = 1,
            ChallengeType = 0,
            ForbidMoving = country.Settings.ForbidMoving,
            ForbidRotating = country.Settings.ForbidRotating,
            ForbidZooming = country.Settings.ForbidZooming,
            Map = country.MapId,
            TimeLimit = country.Settings.TimeLimit
        };

        var response = await client.CreateChallengeAsync(request, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.Token))
        {
            // A challenge without a token cannot be played or linked; treat it like a failure.
            throw new InvalidOperationException("GeoGuessr returned a challenge without a token.");
        }

        return response.Token;
    }

    private async Task<bool> TryAnnounceAsync(
        CountryChallengePlan plan,
        ChannelAnnouncement announcement,
        CancellationToken cancellationToken)
    {
        ulong messageId;
        try
        {
            messageId = await discordMessageAccess
                .SendMessageAsync(announcement.Message.Content, announcement.ChannelId, announcement.Message.Mentions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogNotAnnounced(logger, ex, announcement.ChannelId);
            return false;
        }

        if (announcement.ThreadName is not null)
        {
            try
            {
                await discordMessageAccess
                    .CreateThreadAsync(announcement.ChannelId, messageId, announcement.ThreadName, plan.Announcement.Thread.AutoArchive, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The challenges are out; a missing thread is not worth taking them back for.
                LogThreadNotCreated(logger, ex, announcement.ChannelId);
            }
        }

        return true;
    }

    /// <summary>
    /// Removes challenges that were stored but never announced. A later run can then post them, and the
    /// pool rotation does not count a country nobody got to play.
    /// </summary>
    private async Task ForgetAsync(List<CountryChallengePost> posts, CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return;
        }

        repository.RemovePosts(posts);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogNotForgotten(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not create the country challenge '{challengeName}' ({country}).")]
    static partial void LogCreationFailed(
        ILogger<AnnounceCountryChallengesHandler> logger,
        Exception exception,
        string challengeName,
        string country);

    [LoggerMessage(LogLevel.Error, "Could not store the country challenges of {date}; they will not be announced.")]
    static partial void LogNotStored(ILogger<AnnounceCountryChallengesHandler> logger, Exception exception, DateOnly date);

    [LoggerMessage(LogLevel.Error, "Could not announce the country challenges in channel {channelId}.")]
    static partial void LogNotAnnounced(ILogger<AnnounceCountryChallengesHandler> logger, Exception exception, ulong channelId);

    [LoggerMessage(LogLevel.Warning, "Could not open the discussion thread of the country challenges in channel {channelId}.")]
    static partial void LogThreadNotCreated(ILogger<AnnounceCountryChallengesHandler> logger, Exception exception, ulong channelId);

    [LoggerMessage(LogLevel.Error, "Could not remove country challenges that were never announced; they will not be retried today.")]
    static partial void LogNotForgotten(ILogger<AnnounceCountryChallengesHandler> logger, Exception exception);
}
