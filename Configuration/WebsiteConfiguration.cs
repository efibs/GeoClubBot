namespace Configuration;

/// <summary>
/// The public stats endpoint (<c>GET /api/v1/stats</c>) the club website reads its numbers from.
/// </summary>
public class WebsiteConfiguration
{
    public const string SectionName = "Website";

    /// <summary>When false, the endpoint answers 404.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The club the website shows next to the main club. Required when <see cref="Enabled"/>, and it
    /// must be another entry of GeoGuessr:Clubs: a club is read with its own token.
    /// </summary>
    public Guid? SecondClubId { get; set; }
}
