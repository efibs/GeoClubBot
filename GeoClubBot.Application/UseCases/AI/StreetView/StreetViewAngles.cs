using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.StreetView;

/// <summary>
/// Which views of a panorama show what the player saw. Pure.
///
/// A round the player could look around in is shown as four views a quarter-turn apart, starting where
/// the round started; one they could not rotate is shown as the single view they had, at its heading,
/// pitch and zoom. Four is what the free thumbnail endpoint and a free vision model handle well: the
/// endpoint serves at most 1024 pixels across, so fewer, wider views would show each clue smaller.
/// </summary>
public static class StreetViewAngles
{
    /// <summary>Each view of a whole panorama covers a quarter of it.</summary>
    public const double QuarterFieldOfView = 90;

    /// <summary>
    /// GeoGuessr's horizontal field of view at zoom 0, as GeoNN measured it against the game: each zoom
    /// level halves the tangent of half the angle.
    /// </summary>
    public const double FieldOfViewAtZoomZero = 126.87;

    /// <summary>The views of a round, as the game showed it.</summary>
    public static IReadOnlyList<StreetViewAngle> ForRound(PanoramaViewMode mode, double? heading, double? pitch, double? zoom)
    {
        var start = Normalise(heading ?? 0);
        return mode == PanoramaViewMode.FixedView
            ? [new StreetViewAngle(start, Math.Clamp(pitch ?? 0, -90, 90), FieldOfView(zoom))]
            : Around(start);
    }

    /// <summary>
    /// The views of a place a Maps link names: where the link looks first, as it shows it, then the rest
    /// of the panorama, since whoever sent it could look around.
    /// </summary>
    public static IReadOnlyList<StreetViewAngle> ForSpot(StreetViewSpot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        var around = Around(Normalise(spot.Heading ?? 0));
        if (spot.Pitch is null && spot.FieldOfView is null)
        {
            return around;
        }

        return
        [
            around[0] with
            {
                Pitch = Math.Clamp(spot.Pitch ?? 0, -90, 90),
                FieldOfView = Math.Clamp(spot.FieldOfView ?? QuarterFieldOfView, 30, FieldOfViewAtZoomZero)
            },
            .. around.Skip(1)
        ];
    }

    /// <summary>GeoGuessr's field of view at a zoom level, in degrees.</summary>
    public static double FieldOfView(double? zoom)
    {
        var halfTangent = Math.Tan(FieldOfViewAtZoomZero * Math.PI / 360) / Math.Pow(2, Math.Max(0, zoom ?? 0));
        return Math.Clamp(Math.Atan(halfTangent) * 360 / Math.PI, 10, FieldOfViewAtZoomZero);
    }

    private static StreetViewAngle[] Around(double start) =>
        [.. Enumerable.Range(0, 4).Select(quarter => new StreetViewAngle(Normalise(start + (quarter * 90)), 0, QuarterFieldOfView))];

    private static double Normalise(double heading) => heading is >= 0 and < 360 ? heading : ((heading % 360) + 360) % 360;
}
