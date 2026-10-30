using FluentAssertions;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class IsoCountriesTests
{
    [Theory]
    [InlineData("MX", "MX")]
    [InlineData("th", "TH")]
    [InlineData("Mexico", "MX")]
    [InlineData("the Netherlands", "NL")]
    [InlineData("USA", "US")]
    [InlineData("Czech Republic", "CZ")]
    [InlineData("Côte d'Ivoire", "CI")]
    [InlineData("Ivory Coast", "CI")]
    [InlineData("São Tomé and Príncipe", "ST")]
    [InlineData("Sao Tome and Principe", "ST")]
    [InlineData("Israel West Bank", "PS")]
    public void Resolve_ReadsCodesNamesAndAliases(string country, string expected) =>
        IsoCountries.Resolve(country).Should().Be(expected);

    [Fact]
    public void Resolve_LeansOnTheGuidesGazetteer_ForRegionsAndTypos()
    {
        var gazetteer = CountryGazetteer.Build(["malaysia", "philippines", "norway", "sweden", "denmark", "finland"]);

        IsoCountries.Resolve("Sabah", gazetteer).Should().Be("MY");
        IsoCountries.Resolve("Philippenes", gazetteer).Should().Be("PH");
        IsoCountries.Resolve("Scandinavia", gazetteer).Should().BeNull("it is several countries, and the tool counts one");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Atlantis")]
    [InlineData("ZZ")]
    public void Resolve_NamesNoCountry_ForWhatIsNone(string? country) =>
        IsoCountries.Resolve(country).Should().BeNull();

    [Fact]
    public void NameOf_GivesTheEnglishName_OrTheCodeWhenThereIsNone()
    {
        IsoCountries.NameOf("PH").Should().Be("Philippines");
        IsoCountries.NameOf("mm").Should().Be("Myanmar");
        IsoCountries.NameOf("ZZ").Should().Be("ZZ");
        IsoCountries.IsCountry("ZZ").Should().BeFalse("GBIF's code for an unknown country");
    }
}
