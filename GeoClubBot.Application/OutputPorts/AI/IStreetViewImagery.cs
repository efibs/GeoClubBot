namespace UseCases.OutputPorts.AI;

/// <summary>One direction to look in from a panorama.</summary>
/// <param name="Heading">Compass degrees, 0 = north, clockwise.</param>
/// <param name="Pitch">Degrees above the horizon (positive looks up), as GeoGuessr and Google Maps give it.</param>
/// <param name="FieldOfView">Horizontal field of view in degrees.</param>
public sealed record StreetViewAngle(double Heading, double Pitch, double FieldOfView);

/// <summary>A view of a panorama as a picture.</summary>
/// <param name="Content">The picture's bytes.</param>
/// <param name="ContentType">Its media type, e.g. <c>image/jpeg</c>.</param>
/// <param name="Angle">The direction it looks in.</param>
public sealed record StreetViewPicture(byte[] Content, string ContentType, StreetViewAngle Angle);

/// <summary>
/// Pictures of a Street View panorama, looked at in given directions: what a round showed the player.
/// Free: read from the endpoint Google Maps itself draws its thumbnails from, with no key, as GeoNN
/// reads panoramas. Never throws: a view that cannot be read is left out, and a panorama Google no
/// longer serves gives none.
/// </summary>
public interface IStreetViewImagery
{
    Task<IReadOnlyList<StreetViewPicture>> ReadViewsAsync(
        string panoId,
        IReadOnlyList<StreetViewAngle> angles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Follows a shortened Google Maps link (<c>maps.app.goo.gl/…</c>) to the full one, which carries the
/// place and the panorama. Only Google's own link shorteners are followed, and only to Google Maps:
/// the bot never fetches an arbitrary address a member pastes.
/// </summary>
public interface IMapsLinkResolver
{
    /// <summary>The full Google Maps link, or null when the link is no Google short link or leads nowhere usable. Never throws.</summary>
    Task<string?> ResolveAsync(string shortLink, CancellationToken cancellationToken = default);
}
