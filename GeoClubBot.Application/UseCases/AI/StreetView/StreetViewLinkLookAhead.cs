using System.Text;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Progress;
using UseCases.UseCases.AI.Review;
using Utilities;

namespace UseCases.UseCases.AI.StreetView;

/// <param name="Hits">The excerpts to lead the answer's: the place as checked, then the guides' clues it was seen to show and those to look for.</param>
/// <param name="Notices">Lines for the member beneath the answer.</param>
public sealed record StreetViewLinkOffer(IReadOnlyList<KnowledgeHit> Hits, IReadOnlyList<string> Notices);

/// <summary>
/// A question that carries a Google Maps Street View link ("what gives this away? maps.app.goo.gl/…")
/// is about that place (#480): the link is followed, the place found in its country and region, the
/// guides searched for its clues, and the panorama checked against them by a free vision model, the way
/// a review checks a round. What it was seen to show leads the answer's excerpts.
///
/// Costs two requests of the day's allowance, the clues' embedding and the vision request, and only for
/// a question with such a link. Anything that goes wrong offers nothing, or a notice, and the question is
/// answered as before. Not run beside the question's own search: the check claims its request through
/// the database context the search's planner uses, which takes one operation at a time.
/// </summary>
public sealed partial class StreetViewLinkLookAhead(
    IMapsLinkResolver mapsLinks,
    IRegionBoundaries boundaries,
    RoundClueFinder clueFinder,
    ClueSightCheck sightCheck,
    IImageRelay imageRelay,
    IAiBudgetRepository budget,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiRoundReviewConfiguration> reviewConfiguration,
    ILogger<StreetViewLinkLookAhead> logger)
{
    public const string SourceTitle = "Street View link";

    /// <summary>Said beneath an answer whose link was checked.</summary>
    public const string CheckedNotice =
        "The Street View link was checked by a free vision model against the guides' clues; it can miss or mistake one.";

    /// <summary>Excerpts the place offers at most, its own included.</summary>
    private const int MaxExcerpts = 8;

    /// <summary>
    /// The offer for a question, or null when it carries no Maps link, the feature is off, or it comes with
    /// a picture: then the question is about the picture, and a Maps link beside it says where it was taken
    /// (what the plant identification weighs its candidates by), not which place to explain.
    /// </summary>
    public async Task<StreetViewLinkOffer?> ReadAsync(string question, bool withPicture, DateOnly budgetDay, CancellationToken cancellationToken)
    {
        var settings = reviewConfiguration.Value;
        var ai = aiConfiguration.Value;
        if (withPicture || !ai.Active || !settings.Enabled || !settings.StreetViewLinksInQuestions || StreetViewLink.Find(question) is not { } link)
        {
            return null;
        }

        try
        {
            return await OfferAsync(link, budgetDay, settings, ai, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception);
            return null;
        }
    }

    private async Task<StreetViewLinkOffer> OfferAsync(
        string link,
        DateOnly budgetDay,
        AiRoundReviewConfiguration settings,
        AiConfiguration ai,
        CancellationToken cancellationToken)
    {
        var url = link;
        if (StreetViewLink.IsShortLink(link))
        {
            using (AiProgress.Step(AiProgressKind.OpeningMapsLink))
            {
                url = await mapsLinks.ResolveAsync(link, cancellationToken).ConfigureAwait(false);
            }

            if (url is null)
            {
                return Notice("The Maps link could not be opened, so the place was not looked at.");
            }
        }

        if (StreetViewLink.Read(url) is not { PanoId: not null } spot)
        {
            return Notice("The Maps link shows no Street View panorama, so the place was not looked at; send the link of a Street View view.");
        }

        if (await boundaries.LocateAsync(spot.Latitude, spot.Longitude, cancellationToken).ConfigureAwait(false) is not { } place)
        {
            return Notice("The Street View link's place could not be placed in a country, so it was not checked against the guides.");
        }

        var round = new ReviewedRound(1, place.CountryCode, place.RegionName, null, null, null, null)
        {
            Played = false,
            Panorama = new RoundPanorama(spot.PanoId!, StreetViewAngles.ForSpot(spot), spot.MapsUrl)
        };

        var embeddings = clueFinder.EmbeddingRequests([round]);
        if (!await budget.TryReserveRequestsAsync(budgetDay, embeddings, ai.OpenRouter.DailyRequestBudget, cancellationToken).ConfigureAwait(false))
        {
            return Notice("Today's AI allowance could not pay for looking at the Street View link; the answer goes without it.");
        }

        IReadOnlyList<IReadOnlyList<RoundClue>> clues;
        using (AiProgress.Step(AiProgressKind.SearchingGuides))
        {
            clues = await clueFinder.FindAsync([round], cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PlaceSight?> sights;
        using (AiProgress.Step(AiProgressKind.LookingAtRounds, "the linked panorama"))
        {
            sights = await sightCheck.LookAsync(
                    [new SightRequest(spot.PanoId!, round.Panorama.Angles, round.CountryName, round.Region, null, [.. clues[0].Select(clue => clue.Hit)])],
                    budgetDay,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var (hits, rounds) = ReviewExcerpts.Assemble(
            [round], clues, sights, settings.WellPlayedPoints, Math.Max(2, settings.SightCheck.UnseenCluesForMissedRounds), MaxExcerpts - 1);
        var sight = sights[0];
        var evidence = rounds[0].Evidence;

        string? picture = null;
        if (imageRelay.IsEnabled && sight?.BestPicture is { } best)
        {
            var stored = await imageRelay.StoreAsync(best.Content, best.ContentType, cancellationToken).ConfigureAwait(false);
            picture = stored.IsSuccess ? stored.Value : null;
        }

        var placeHit = new KnowledgeHit(
            DeterministicGuid.FromName("street-view-link", $"{spot.PanoId}|{place.CountryCode}"),
            Score: 0f,
            picture is null ? KnowledgeChunkKind.Text : KnowledgeChunkKind.Image,
            Describe(rounds[0], evidence, hits),
            spot.MapsUrl,
            picture,
            SourceTitle,
            Country: null,
            SectionPath: $"The linked place: {round.PlaceName}",
            Author: null,
            Priority: 0);

        LogOffered(logger, place.CountryCode, evidence?.Checked == true, evidence?.Seen.Count ?? 0, hits.Count);
        return new StreetViewLinkOffer(
            [placeHit, .. hits],
            evidence?.Checked == true ? [CheckedNotice] : ["The Street View link could not be looked at just now; the answer names what to look for there, not what it shows."]);
    }

    /// <summary>
    /// What the model is told of the place: where it is, which of the guides' clues it was seen to show
    /// (named by their headings, since the excerpts are numbered only once the answer's are added), and
    /// how to use them.
    /// </summary>
    public static string Describe(ReviewedRound round, RoundEvidence? evidence, IReadOnlyList<KnowledgeHit> hits)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(hits);

        string Heading(int marker) =>
            marker >= 1 && marker <= hits.Count && SectionLabel.Clean(hits[marker - 1].SectionPath) is { Length: > 0 } label
                ? $"\"{label}\""
                : "a guide excerpt below";

        var text = new StringBuilder();
        text.Append("The member's Google Maps link is a Street View panorama in ").Append(round.PlaceName).Append('.');
        if (evidence is not { Checked: true })
        {
            text.Append(" Its pictures could not be looked at, so nothing is known of what it shows. Say so, and give the clues of ")
                .Append(round.CountryName).Append(" to look for there from the guide excerpts below.");
            return text.ToString();
        }

        text.Append(" A vision model checked its views against the guides' clues for the place.");
        text.Append(evidence.Seen.Count == 0
            ? " It saw none of them."
            : $" It saw {string.Join("; ", evidence.Seen.Select(seen => $"{Heading(seen.Marker)}{(string.IsNullOrWhiteSpace(seen.Where) ? string.Empty : $" ({seen.Where})")}"))}.");

        if (evidence.AlsoSeen.Count > 0)
        {
            text.Append(" Also seen, not from the guides: ").Append(string.Join("; ", evidence.AlsoSeen)).Append('.');
        }

        if (evidence.ToLookFor.Count > 0)
        {
            text.Append(" Not seen there, but clues of ").Append(round.CountryName).Append(" to look for: ")
                .Append(string.Join("; ", evidence.ToLookFor.Select(Heading))).Append('.');
        }

        text.Append(" Explain what gives the place away from the clues it was seen to show, the country's first, citing their excerpts. "
                    + "Never say it shows a clue it was not seen to show. Cite this excerpt to show the place.");
        return text.ToString();
    }

    private static StreetViewLinkOffer Notice(string notice) => new([], [notice]);

    [LoggerMessage(LogLevel.Information, "A Street View link in {Country} was offered to a question: checked {Checked}, {Seen} clue(s) seen, {Excerpts} excerpt(s).")]
    static partial void LogOffered(ILogger logger, string country, bool @checked, int seen, int excerpts);

    [LoggerMessage(LogLevel.Warning, "The Street View link in a question could not be looked at; the question is answered without it.")]
    static partial void LogFailed(ILogger logger, Exception exception);
}
