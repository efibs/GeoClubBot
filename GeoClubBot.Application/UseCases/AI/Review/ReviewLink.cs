using System.Text.RegularExpressions;
using UseCases.UseCases.AI.StreetView;

namespace UseCases.UseCases.AI.Review;

/// <summary>What a pasted link names for a review.</summary>
public enum ReviewLinkKind
{
    /// <summary>A challenge (<c>geoguessr.com/challenge/…</c>): the member's own game in it is reviewed.</summary>
    Challenge,

    /// <summary>A single game (<c>geoguessr.com/game/…</c>), whoever played it.</summary>
    Game,

    /// <summary>
    /// A results page (<c>geoguessr.com/results/…</c>) or a bare token. GeoGuessr shows a challenge's
    /// results and a single game's under the same path, so it is read as a game, then as a challenge.
    /// </summary>
    Results,

    /// <summary>A Google Maps link to one Street View place (<see cref="ReviewLink.Value"/> is the link).</summary>
    StreetView,

    /// <summary>A shortened Google Maps link (<c>maps.app.goo.gl/…</c>), to be followed before it says anything.</summary>
    StreetViewShortLink
}

/// <param name="Kind">What the link names.</param>
/// <param name="Value">The GeoGuessr token, or the Google Maps link.</param>
public sealed record ReviewLink(ReviewLinkKind Kind, string Value)
{
    public bool IsStreetView => Kind is ReviewLinkKind.StreetView or ReviewLinkKind.StreetViewShortLink;
}

/// <summary>
/// Reads what a member pastes into <c>/ai review</c>: a challenge link, a game or results link, a token
/// on its own, or a Google Maps link to a Street View place. Pure.
/// </summary>
public static partial class ReviewLinks
{
    /// <summary>The link, or null when the text holds none the review can read.</summary>
    public static ReviewLink? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().Trim('<', '>');

        var geoGuessr = GeoGuessrLink().Match(trimmed);
        if (geoGuessr.Success)
        {
            var kind = geoGuessr.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "challenge" => ReviewLinkKind.Challenge,
                "game" => ReviewLinkKind.Game,
                _ => ReviewLinkKind.Results
            };
            return new ReviewLink(kind, geoGuessr.Groups["token"].Value);
        }

        if (StreetViewLink.Find(trimmed) is { } maps)
        {
            return new ReviewLink(StreetViewLink.IsShortLink(maps) ? ReviewLinkKind.StreetViewShortLink : ReviewLinkKind.StreetView, maps);
        }

        return BareToken().IsMatch(trimmed) ? new ReviewLink(ReviewLinkKind.Results, trimmed) : null;
    }

    [GeneratedRegex(@"geoguessr\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?(?<kind>challenge|results|game)/(?<token>[A-Za-z0-9]{8,64})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeoGuessrLink();

    [GeneratedRegex(@"^[A-Za-z0-9]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BareToken();
}
