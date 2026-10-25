using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// Where a plant species is recorded, read from a biodiversity database (GBIF).
///
/// The guides cover the plants players meet, not where each one grows: asked where Khasi pines are
/// found, the bot had two guides on India and Vietnam and nothing on Thailand or the Philippines, which
/// hold more records of the tree than either (2026-10-23 feedback). A distribution is a fact the
/// database has and the index never will.
/// </summary>
public interface ISpeciesRangeReader
{
    /// <summary>
    /// The species' records counted by country, or by region within <see cref="SpeciesRangeQuery.CountryCode"/>.
    /// Fails with <see cref="ErrorType.NotFound"/> when no plant goes by that name, and with
    /// <see cref="ErrorType.Unexpected"/> when the database could not be read; never throws for either.
    /// </summary>
    Task<Result<SpeciesRange>> ReadAsync(SpeciesRangeQuery query, CancellationToken cancellationToken = default);
}

/// <param name="Name">A scientific or common name, as the model wrote it: "Pinus kesiya", "khasi pine".</param>
/// <param name="CountryCode">An ISO 3166 alpha-2 code to break the records down by region, or null for every country.</param>
public sealed record SpeciesRangeQuery(string Name, string? CountryCode);

/// <param name="TaxonKey">The database's id for the species, which its public page is addressed by.</param>
/// <param name="ScientificName">The name the database matched, with its author: "Pinus kesiya Royle ex Gordon".</param>
/// <param name="CanonicalName">The same name without its author: "Pinus kesiya".</param>
/// <param name="Rank">"SPECIES", "GENUS", …</param>
/// <param name="MatchedByCommonName">The name was a common one, matched through the species' vernacular names.</param>
/// <param name="FuzzyMatch">The name matched only approximately, e.g. a misspelling.</param>
/// <param name="CountryCode">The country the areas lie in, or null when the areas are countries.</param>
/// <param name="Areas">Countries (as ISO alpha-2 codes) or regions within one, most records first.</param>
/// <param name="TotalRecords">Every record of the species in the scope asked for, including those of no listed area.</param>
public sealed record SpeciesRange(
    long TaxonKey,
    string ScientificName,
    string CanonicalName,
    string Rank,
    bool MatchedByCommonName,
    bool FuzzyMatch,
    string? CountryCode,
    IReadOnlyList<SpeciesRangeArea> Areas,
    long TotalRecords);

/// <param name="Name">An ISO alpha-2 country code, or a region's name as the records spell it.</param>
public sealed record SpeciesRangeArea(string Name, long Records);
