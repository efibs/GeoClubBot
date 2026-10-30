using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Tools;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The species excerpt as the model reads it, pinned whole: the counts are only safe to show with their
/// caveat, and the figures are GBIF's own for the trees the 2026-10-23 feedback asked about.
/// </summary>
public sealed class SpeciesRangeExcerptTests
{
    [Fact]
    public Task ListsTheCountries_WithTheCaveat()
    {
        var range = new SpeciesRange(
            5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: true, FuzzyMatch: false,
            CountryCode: null,
            [
                new("PH", 256), new("CN", 105), new("TH", 87), new("IN", 80), new("VN", 66), new("MM", 60), new("MG", 36),
                new("ZZ", 26), new("ID", 25), new("BR", 19), new("LA", 14)
            ],
            TotalRecords: 1061);

        return Verify(Render(range, "khasi pine"));
    }

    [Fact]
    public Task ListsTheRegionsOfOneCountry_CountingASpellingOnce()
    {
        var range = new SpeciesRange(
            2775707, "Yucca filifera Chabaud", "Yucca filifera", "SPECIES", MatchedByCommonName: false, FuzzyMatch: false,
            CountryCode: "MX",
            [
                new("Nuevo León", 384), new("Zacatecas", 247), new("San Luis Potosí", 193), new("Guanajuato", 115),
                new("Querétaro", 93), new("Queretaro", 34), new("San luis potosi", 28)
            ],
            TotalRecords: 1609);

        return Verify(Render(range, "Yucca filifera"));
    }

    [Fact]
    public void SaysSo_WhenThereAreNoRecords()
    {
        var range = new SpeciesRange(1, "Rara avis L.", "Rara avis", "SPECIES", false, FuzzyMatch: true, "PE", [], 0);

        var hit = SpeciesRangeExcerpt.ToHit(range, "rara avs", maxAreas: 15);

        hit.Text.Should().Contain("\"rara avs\" was read as this one").And.Contain("no occurrence records of it in Peru")
            .And.NotContain("These count", "there are no counts to qualify");
    }

    [Fact]
    public void IsTheSameExcerpt_ForTheSameSpeciesAndScope()
    {
        var range = new SpeciesRange(5285824, "Pinus kesiya", "Pinus kesiya", "SPECIES", false, false, null, [new("PH", 1)], 1);

        SpeciesRangeExcerpt.ToHit(range, "a", 15).Id.Should().Be(SpeciesRangeExcerpt.ToHit(range, "b", 15).Id);
        SpeciesRangeExcerpt.ToHit(range with { CountryCode = "PH" }, "a", 15).Id
            .Should().NotBe(SpeciesRangeExcerpt.ToHit(range, "a", 15).Id);
    }

    /// <summary>The excerpt numbered as the model is shown it.</summary>
    private static string Render(SpeciesRange range, string askedName) =>
        AiPromptBuilder.FormatExcerpt(SpeciesRangeExcerpt.ToHit(range, askedName, maxAreas: 15), 11, out _);
}
