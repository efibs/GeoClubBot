using FluentAssertions;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Finding the countries a question is about, as the index spells them. The spellings are the
/// production index's own, read on 2026-10-23: the same country under two names is the case to get
/// right, because a boost that searches one of them misses the other's chunks.
/// </summary>
public sealed class CountryGazetteerTests
{
    private static readonly string[] IndexCountries =
    [
        "usa", "united states", "czechia", "czech republic", "slovakia", "são tomé & príncipe",
        "sao tome and principe", "malaysia", "indonesia", "philippines", "australia", "japan", "india",
        "brazil", "mexico", "norway", "sweden", "finland", "denmark", "iceland", "chile", "greece",
        "united kingdom", "nmi", "israel west bank", "spillover countries", "Kyrgyzstan"
    ];

    private static readonly CountryGazetteer Gazetteer = CountryGazetteer.Build(IndexCountries);

    [Theory]
    [InlineData("which states in the US don't require front plates?")]
    [InlineData("is this road in the USA?")]
    [InlineData("United States stop signs")]
    [InlineData("are U.S. plates on the front?")]
    public void FindMentions_FindsEverySpellingOfACountryTheIndexFilesTwice(string question) =>
        Gazetteer.FindMentions(question).Should().ContainSingle()
            .Which.IndexNames.Should().BeEquivalentTo(["usa", "united states"]);

    [Fact]
    public void FindMentions_DoesNotTakeThePronounForTheCountry() =>
        Gazetteer.FindMentions("can you tell us where this is?").Should().BeEmpty();

    [Theory]
    [InlineData("Can I use this to differentiate it from Czechia?")]
    [InlineData("Czech Republic or Slovakia?")]
    [InlineData("is that a czech guardrail")]
    public void FindMentions_MergesCzechiaAndTheCzechRepublic(string question) =>
        Gazetteer.FindMentions(question).Should().Contain(mention => mention.IndexNames.Contains("czechia")
                                                                     && mention.IndexNames.Contains("czech republic"));

    [Fact]
    public void FindMentions_FoldsAccentsAndAmpersands() =>
        Gazetteer.FindMentions("Sao Tome & Principe bollards").Should().ContainSingle()
            .Which.IndexNames.Should().BeEquivalentTo(["são tomé & príncipe", "sao tome and principe"]);

    [Theory]
    [InlineData("where do they grow rice in the Philippenes?", "philippines")]
    [InlineData("where can I find sugar cane in the Phillipines?", "philippines")]
    // One letter off both "philippine" and "philippines": two names, but one country.
    [InlineData("corn in the philippinee", "philippines")]
    [InlineData("Ghanaian or Swedish snow poles?", "sweden")]
    [InlineData("is red soil in malaysia regional", "malaysia")]
    [InlineData("How do I identify Talas, Kyrgyzstan ?", "kyrgyzstan")]
    public void FindMentions_ReadsTyposAdjectivesAndCase(string question, string expected) =>
        Gazetteer.FindMentions(question).Select(mention => mention.Name).Should().Contain(expected);

    [Theory]
    [InlineData("how can I clearly identify Sabah?", new[] { "malaysia" })]
    [InlineData("an insulator from Kyushu", new[] { "japan" })]
    [InlineData("snow poles in scandinavia", new[] { "norway", "sweden", "denmark", "finland" })]
    [InlineData("what plants grow in Baja California?", new[] { "mexico" })]
    public void FindMentions_PlacesARegionInItsCountries(string question, string[] expected) =>
        Gazetteer.FindMentions(question).Select(mention => mention.Name).Should().BeEquivalentTo(expected);

    [Theory]
    [InlineData("is New South Wales the only state with these?", "australia")]
    [InlineData("New Mexico or Chile?", "united states")]
    public void FindMentions_LetsTheLongerNameWin(string question, string expected) =>
        Gazetteer.FindMentions(question).Select(mention => mention.Name).First().Should().Be(expected);

    [Theory]
    [InlineData("while driving I saw a green pole")]
    [InlineData("the walls are white")]
    [InlineData("which countries have spillover countries?")]
    public void FindMentions_ForgivesNoTypoInAShortWord_AndNamesNoPseudoCountry(string question) =>
        Gazetteer.FindMentions(question).Should().BeEmpty();

    [Fact]
    public void FindMentions_NamesOnlyCountriesTheIndexHolds() =>
        // Germany is a built-in name, but nothing in this index is filed under it: nothing to boost.
        Gazetteer.FindMentions("German bollards").Should().BeEmpty();

    [Fact]
    public void FindMentions_ListsEachCountryOnce_InOrderOfMention() =>
        Gazetteer.FindMentions("Thailand vs Philippines vs the philippines, or Malaysia?")
            .Select(mention => mention.Name).Should().Equal("philippines", "malaysia");

    [Theory]
    [InlineData("USA", "united states")]
    [InlineData("Czech Republic", "czechia")]
    [InlineData("Malaysia (Sabah)", "malaysia")]
    [InlineData("NMI", "northern mariana islands")]
    [InlineData("West Bank", "israel west bank")]
    public void Resolve_ReadsTheNamesAPlannerGives(string name, string expected) =>
        Gazetteer.Resolve(name).Select(mention => mention.Name).Should().Contain(expected);

    [Fact]
    public void Resolve_ReturnsNothingForAPlaceItDoesNotKnow() =>
        Gazetteer.Resolve("Atlantis").Should().BeEmpty();

    [Fact]
    public void Build_AddsConfiguredAliases_WhichMayCorrectABuiltInName()
    {
        var gazetteer = CountryGazetteer.Build(IndexCountries, new Dictionary<string, List<string>>
        {
            ["borneo"] = ["Malaysia"],
            ["peninsular"] = ["malaysia"],
            ["nowhere"] = ["atlantis"]
        });

        gazetteer.FindMentions("lamps in Borneo").Select(mention => mention.Name).Should().Equal("malaysia");
        gazetteer.FindMentions("peninsular lamps").Select(mention => mention.Name).Should().Equal("malaysia");
        gazetteer.FindMentions("nowhere").Should().BeEmpty("an alias for no country the index holds names nothing");
    }

    [Fact]
    public void Empty_FindsNothing() =>
        CountryGazetteer.Empty.FindMentions("Brazil").Should().BeEmpty();
}
