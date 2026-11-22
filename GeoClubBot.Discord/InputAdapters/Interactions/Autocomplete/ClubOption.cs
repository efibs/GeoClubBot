namespace GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;

/// <summary>
/// Reads a club option filled by <see cref="ClubAutocompleteHandler"/>, whose value is the club id.
/// Discord sends whatever was typed when no suggestion is picked, so the value may be no id at all.
/// </summary>
internal static class ClubOption
{
    public const string UnknownClubMessage = "Unknown club. Please pick one of the suggested clubs.";

    /// <summary>
    /// The club id of an option value, null when the option was left out (the main club).
    /// False when something was typed that is not a suggested club.
    /// </summary>
    public static bool TryParse(string? value, out Guid? clubId)
    {
        clubId = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out var parsed))
        {
            return false;
        }

        clubId = parsed;
        return true;
    }
}
