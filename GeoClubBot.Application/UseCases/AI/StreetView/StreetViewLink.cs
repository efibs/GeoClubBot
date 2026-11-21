using System.Globalization;
using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.StreetView;

/// <summary>A place in Street View, as a Google Maps link names it.</summary>
/// <param name="PanoId">Google's panorama id; null when the link shows a map, not a panorama.</param>
/// <param name="Latitude">Where it is.</param>
/// <param name="Longitude">Where it is.</param>
/// <param name="Heading">The direction the link looks in, compass degrees; null when it does not say.</param>
/// <param name="Pitch">Degrees above the horizon (positive looks up); null when it does not say.</param>
/// <param name="FieldOfView">The view's horizontal field of view in degrees; null when it does not say.</param>
public sealed record StreetViewSpot(
    string? PanoId,
    double Latitude,
    double Longitude,
    double? Heading = null,
    double? Pitch = null,
    double? FieldOfView = null)
{
    /// <summary>A link that opens Google Maps' Street View at the spot, looking where it looked.</summary>
    public string MapsUrl => StreetViewLink.ToMapsUrl(this);
}

/// <summary>
/// Reads Google Maps links to Street View — the long form
/// (<c>google.com/maps/@34.05,-99.96,3a,90y,51.57h,92.16t/data=!3m7!1e1!3m5!1s&lt;pano&gt;!2e0…</c>), the
/// documented one (<c>…/maps/@?api=1&amp;map_action=pano&amp;pano=…</c>) — and recognises the shortened
/// ones (<c>maps.app.goo.gl/…</c>), which only <see cref="OutputPorts.AI.IMapsLinkResolver"/> can open. Pure.
///
/// In the long form the view is written after the place: <c>90y</c> the field of view, <c>51.57h</c> the
/// heading and <c>92.16t</c> the tilt, where 90 looks at the horizon (pitch = tilt − 90). The panorama
/// is the <c>!1s</c> value followed by <c>!2e</c>; <c>!2e0</c> is Google's own coverage.
/// </summary>
public static partial class StreetViewLink
{
    /// <summary>The first Google Maps link in <paramref name="text"/>, long or short; null when there is none.</summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = AnyLink().Match(text);
        return match.Success ? match.Value.TrimEnd('>', ')', ']', '.', ',', '!', '?', '"', '\'') : null;
    }

    /// <summary>Whether <paramref name="url"/> is one of Google's shortened Maps links, which carry nothing until followed.</summary>
    public static bool IsShortLink(string? url) =>
        url is not null && ShortLink().IsMatch(url.Trim());

    /// <summary>Whether <paramref name="url"/> is a link to Google Maps itself (not a short link).</summary>
    public static bool IsMapsLink(string? url) =>
        url is not null && LongLink().IsMatch(url.Trim());

    /// <summary>The place and view of a long Google Maps link; null when it is none, or names no place.</summary>
    public static StreetViewSpot? Read(string? url)
    {
        if (!IsMapsLink(url))
        {
            return null;
        }

        var link = Uri.UnescapeDataString(url!.Trim());

        double? latitude = null, longitude = null, heading = null, pitch = null, fov = null;
        var at = AtView().Match(link);
        if (at.Success)
        {
            latitude = Number(at.Groups["lat"].Value);
            longitude = Number(at.Groups["lng"].Value);
            foreach (Capture capture in at.Groups["part"].Captures)
            {
                var part = capture.Value;
                if (part.Length < 2 || Number(part[..^1]) is not { } value)
                {
                    continue;
                }

                switch (part[^1])
                {
                    case 'h':
                        heading = Normalise(value);
                        break;
                    case 't':
                        pitch = Math.Clamp(value - 90, -90, 90);
                        break;
                    case 'y':
                        fov = value;
                        break;
                }
            }
        }

        // The documented form, and older links: viewpoint= / cbll= for the place, heading= / pitch= / fov=.
        if (latitude is null && Pair().Match(link) is { Success: true } pair)
        {
            latitude = Number(pair.Groups["lat"].Value);
            longitude = Number(pair.Groups["lng"].Value);
        }

        heading ??= QueryNumber(link, "heading") is { } h ? Normalise(h) : null;
        pitch ??= QueryNumber(link, "pitch");
        fov ??= QueryNumber(link, "fov");

        var panoId = PanoInData().Match(link) is { Success: true } data && data.Groups["kind"].Value == "0"
            ? data.Groups["pano"].Value
            : PanoInQuery().Match(link) is { Success: true } query
                ? query.Groups["pano"].Value
                : null;

        if (latitude is not { } lat || longitude is not { } lng || lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return null;
        }

        return new StreetViewSpot(panoId, lat, lng, heading, pitch, fov is > 0 and <= 180 ? fov : null);
    }

    /// <summary>
    /// The documented Maps URL for a panorama (<c>map_action=pano</c>), which opens Street View there,
    /// looking where the spot looks; a map link at the place when there is no panorama.
    /// </summary>
    public static string ToMapsUrl(StreetViewSpot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        var place = $"{Format(spot.Latitude)},{Format(spot.Longitude)}";
        if (spot.PanoId is null)
        {
            return $"https://www.google.com/maps/search/?api=1&query={place}";
        }

        var url = $"https://www.google.com/maps/@?api=1&map_action=pano&pano={Uri.EscapeDataString(spot.PanoId)}&viewpoint={place}";
        if (spot.Heading is { } heading)
        {
            url += $"&heading={Format(heading)}";
        }

        if (spot.Pitch is { } pitch)
        {
            url += $"&pitch={Format(pitch)}";
        }

        if (spot.FieldOfView is { } fov)
        {
            url += $"&fov={Format(Math.Clamp(fov, 10, 100))}";
        }

        return url;
    }

    private static double Normalise(double heading) => heading is >= 0 and < 360 ? heading : ((heading % 360) + 360) % 360;

    /// <summary>Seven decimals, as Google writes a place: about a centimetre.</summary>
    private static string Format(double value) => Math.Round(value, 7).ToString("0.#######", CultureInfo.InvariantCulture);

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static double? QueryNumber(string link, string name)
    {
        var match = Regex.Match(link, $@"[?&]{name}=(?<value>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? Number(match.Groups["value"].Value) : null;
    }

    [GeneratedRegex(@"https?://(?:(?:www\.|maps\.)?google\.[a-z]{2,3}(?:\.[a-z]{2})?/maps|maps\.app\.goo\.gl/|goo\.gl/maps/)[^\s<>]*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AnyLink();

    [GeneratedRegex(@"^https?://(?:maps\.app\.goo\.gl/[A-Za-z0-9_-]+|goo\.gl/maps/[A-Za-z0-9_-]+)(?:[/?#][^\s]*)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ShortLink();

    [GeneratedRegex(@"^https?://(?:www\.|maps\.)?google\.[a-z]{2,3}(?:\.[a-z]{2})?/maps(?:[/?@#]|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LongLink();

    /// <summary>"@34.0519954,-99.9644448,3a,90y,51.57h,92.16t".</summary>
    [GeneratedRegex(@"@(?<lat>-?\d{1,2}(?:\.\d+)?),(?<lng>-?\d{1,3}(?:\.\d+)?)(?:,(?<part>-?\d+(?:\.\d+)?[a-z]))*",
        RegexOptions.CultureInvariant)]
    private static partial Regex AtView();

    [GeneratedRegex(@"[?&](?:viewpoint|cbll|ll|query)=(?<lat>-?\d{1,2}\.\d+),(?<lng>-?\d{1,3}\.\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Pair();

    /// <summary>"!1s3kZJIREjaW8A1P039qpRvA!2e0": the panorama, and whose coverage it is (0 = Google's).</summary>
    [GeneratedRegex(@"!1s(?<pano>[A-Za-z0-9_-]{16,64})!2e(?<kind>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PanoInData();

    [GeneratedRegex(@"[?&](?:pano|panoid)=(?<pano>[A-Za-z0-9_-]{16,64})(?:&|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PanoInQuery();
}
