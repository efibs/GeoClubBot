using FluentAssertions;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Which questions are looked up in GBIF before the model answers, and by which names. The first
/// cases are the rated questions of the 2026-10-23 export; the species tool was called for none of them
/// in the 2026-10-30 replay.
/// </summary>
public sealed class PlantRangeQuestionTests
{
    [Theory]
    // #25 and #49: "Khasi pines are also found outside of India". GBIF knows the singular only.
    [InlineData("where can I find Khasi pines?", "Khasi pines|Khasi pine")]
    [InlineData(", where can I find Khasi Pines?", "Khasi pines|Khasi pine")]
    // #47: "Agave Lechuguilla" as written matches the genus Agave alone.
    [InlineData("Where can I find agave Lechuguilla", "agave lechuguilla")]
    [InlineData("where does agave lechuguilla grow?", "agave lechuguilla")]
    [InlineData("Where is pampas grass found?", "pampas grass")]
    [InlineData("where else can you see baobabs", "baobabs|baobab")]
    [InlineData("which countries have Joshua trees?", "Joshua trees|Joshua tree|Joshua")]
    [InlineData("In which countries can I find the khasi pine trees?", "khasi pine trees|khasi pine tree|khasi pine")]
    [InlineData("where are eucalyptus trees common", "eucalyptus trees|eucalyptus tree|eucalyptus")]
    [InlineData("where can I find cacti", "cacti|cactus")]
    [InlineData("where can I find Khasi pines outside of India?", "Khasi pines|Khasi pine")]
    [InlineData("where can I find Khasi pines in other countries?", "Khasi pines|Khasi pine")]
    [InlineData("where in the world can I find baobabs?", "baobabs|baobab")]
    [InlineData("where on earth do baobabs grow", "baobabs|baobab")]
    [InlineData("where can I find baobabs in the world", "baobabs|baobab")]
    [InlineData("where can I find baobabs in Africa?", "baobabs|baobab")]
    public void ReadsTheThingAQuestionAsksTheWhereaboutsOf(string question, string expected) =>
        PlantRangeQuestion.NamesAskedAbout(question).Select(name => name.Name)
            .Should().Equal(expected.Split('|'));

    [Theory]
    // #17, #22 and #23: within one country GBIF counts where people recorded a crop, not where it grows.
    [InlineData("where do they grow rice in the Philippenes?")]
    [InlineData("where can I find sugar cane in the Philippenes?")]
    [InlineData("Where are Khasi pines found in Vietnam?")]
    [InlineData("where can I find yucca within Mexico")]
    // #8 asks what grows somewhere; #33 compares two regions.
    [InlineData("what plants grow in Baja California?")]
    [InlineData("what is the best way to differentiate northern European pines from Pacific Northwest pines?")]
    [InlineData("What about corn?")]
    // A pointer or a person is not a name.
    [InlineData("where can I find this?")]
    [InlineData("where can I find these trees")]
    [InlineData("where do they grow it?")]
    [InlineData("where can I find the gen 4 car")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesAQuestionAlone_ThatAsksNoSuchThingOrAsksItAboutOnePlace(string? question) =>
        PlantRangeQuestion.NamesAskedAbout(question).Should().BeEmpty();

    [Fact]
    public void TakesALoneWordLeftByDroppingTree_OnlyByItsScientificName()
    {
        // "dragon" is a common name of tarragon: "dragon trees" must never be read as it.
        var names = PlantRangeQuestion.NamesAskedAbout("where can I find dragon trees?");

        names.Select(name => (name.Name, name.ScientificOnly)).Should().Equal(
            ("dragon trees", false), ("dragon tree", false), ("dragon", true));
    }

    [Fact]
    public void TakesAWordAsWritten_ByAnyName()
    {
        PlantRangeQuestion.NamesAskedAbout("where can I find baobabs?")
            .Should().OnlyContain(name => !name.ScientificOnly);
    }

    [Fact]
    public void ReadsAThingOfAnyPlantShape_LeavingTheSpeciesDatabaseToSayWhetherItIsOne()
    {
        // The question's shape is all that is read; GBIF, which takes only names it can stand behind,
        // turns "yellow bollards" away.
        PlantRangeQuestion.NamesAskedAbout("where can I find yellow bollards?").Select(name => name.Name)
            .Should().Equal("yellow bollards", "yellow bollard");
    }
}
