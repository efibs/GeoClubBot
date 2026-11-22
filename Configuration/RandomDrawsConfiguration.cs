using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// <c>/random</c> (#2): draws of countries, numbers, coins, dice, list items, club members, people in a
/// voice channel or with roles, and random teams.
/// </summary>
public class RandomDrawsConfiguration
{
    public const string SectionName = "RandomDraws";

    /// <summary>Off hides <c>/random</c> from the manual and answers every draw with "switched off".</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The most items one draw may take (the <c>count</c> option), and the most teams.</summary>
    [Range(1, 25)]
    public int MaxCount { get; set; } = 25;

    /// <summary>
    /// ISO 3166-1 alpha-2 codes of the countries <c>/random country coverage-only</c> draws from. Empty
    /// takes the built-in list: every country with a PlonkIt guide, i.e. official Street View coverage.
    /// </summary>
    public List<string> StreetViewCountryCodes { get; set; } = [];
}
