namespace UseCases.OutputPorts.AI;

/// <summary>How the panorama was shown to the player: all around them, or one fixed view.</summary>
public enum PanoramaViewMode
{
    /// <summary>The player could look around (moving, or no moving): the whole panorama is read.</summary>
    FullPanorama,

    /// <summary>No rotating: only the view the round started with, at its heading, pitch and zoom.</summary>
    FixedView
}

/// <param name="PanoId">Google's panorama id, decoded (GeoGuessr lists it hex-encoded, see <c>GeoGuessrPanoId</c>).</param>
/// <param name="Heading">The view's compass heading in degrees; for <see cref="PanoramaViewMode.FixedView"/>.</param>
/// <param name="Pitch">Up or down from the horizon in degrees.</param>
/// <param name="Zoom">GeoGuessr's zoom level, 0 being the widest.</param>
public sealed record PanoramaView(string PanoId, PanoramaViewMode Mode, double? Heading = null, double? Pitch = null, double? Zoom = null);

/// <param name="CountryCode">ISO 3166 alpha-2, upper case.</param>
/// <param name="Probability">Between 0 and 1.</param>
public sealed record CountryProbability(string CountryCode, double Probability);

/// <summary>What a Street View model makes of a panorama: its guess and the countries it weighs, likeliest first.</summary>
public sealed record PanoramaGuess(double Latitude, double Longitude, IReadOnlyList<CountryProbability> Countries, string Model);

/// <summary>
/// A model that guesses where a Street View panorama is (GeoNN). Optional: never throws, and answers
/// null when it is not set up, unreachable or does not know the panorama.
/// </summary>
public interface IStreetViewGuesser
{
    /// <summary>Whether there is a model to ask at all.</summary>
    bool IsAvailable { get; }

    Task<PanoramaGuess?> GuessAsync(PanoramaView view, CancellationToken cancellationToken = default);
}
