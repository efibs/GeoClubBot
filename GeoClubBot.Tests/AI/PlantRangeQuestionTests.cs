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

    [Theory]
    [InlineData("this")]
    [InlineData("that")]
    [InlineData("these")]
    [InlineData("those")]
    [InlineData("it")]
    [InlineData("them")]
    [InlineData("they")]
    [InlineData("one")]
    [InlineData("ones")]
    [InlineData("something")]
    [InlineData("anything")]
    [InlineData("such")]
    [InlineData("which")]
    [InlineData("what")]
    [InlineData("he")]
    [InlineData("she")]
    [InlineData("i")]
    [InlineData("you")]
    [InlineData("we")]
    [InlineData("people")]
    [InlineData("players")]
    [InlineData("more")]
    [InlineData("other")]
    public void NeverReadsAPointerOrThePersonAsking_AsAName(string word) =>
        PlantRangeQuestion.NamesAskedAbout($"where can I find {word}?").Should().BeEmpty();

    [Theory]
    [InlineData("tree")]
    [InlineData("trees")]
    [InlineData("plant")]
    [InlineData("plants")]
    [InlineData("bush")]
    [InlineData("bushes")]
    [InlineData("shrub")]
    [InlineData("shrubs")]
    [InlineData("flower")]
    [InlineData("flowers")]
    public void DropsAWordThatOnlySaysWhatKindOfPlantItIs(string kind) =>
        PlantRangeQuestion.NamesAskedAbout($"where can I find kauri {kind}?")[^1]
            .Should().Be(new PlantRangeName("kauri", ScientificOnly: true));

    [Fact]
    public void KeepsAKindWord_WhenItIsTheWholeName()
    {
        PlantRangeQuestion.NamesAskedAbout("where can I find trees?")
            .Should().Equal(new PlantRangeName("trees", false), new PlantRangeName("tree", false));
    }

    [Fact]
    public void KeepsANameOfSeveralWords_ByAnyName()
    {
        PlantRangeQuestion.NamesAskedAbout("where can I find pampas grass?")
            .Should().Equal(new PlantRangeName("pampas grass", false));
    }

    [Theory]
    [InlineData("daisies", "daisy")]
    [InlineData("pies", "pie")]
    [InlineData("peaches", "peach")]
    [InlineData("bushes", "bush")]
    [InlineData("mosses", "moss")]
    [InlineData("boxes", "box")]
    [InlineData("figs", "fig")]
    public void TriesTheSingular(string written, string singular) =>
        PlantRangeQuestion.NamesAskedAbout($"where can I find {written}?").Select(name => name.Name)
            .Should().Equal(written, singular);

    [Theory]
    [InlineData("moss")]
    [InlineData("cactus")]
    [InlineData("iris")]
    [InlineData("oas")]
    public void LeavesAWordThatOnlyLooksPlural(string written) =>
        PlantRangeQuestion.NamesAskedAbout($"where can I find {written}?").Select(name => name.Name)
            .Should().Equal(written);

    [Theory]
    [InlineData("where do farmers plant rice?")]
    [InlineData("where did people cultivate rice")]
    [InlineData("where does rice grow")]
    [InlineData("where is rice grown")]
    [InlineData("where are rice native")]
    [InlineData("in what countries can you find rice?")]
    [InlineData("which countries is rice found in?")]
    [InlineData("what countries does rice grow in?")]
    [InlineData("which countries are rice native to?")]
    public void ReadsEachShapeOfTheQuestion(string question) =>
        PlantRangeQuestion.NamesAskedAbout(question).Select(name => name.Name).Should().Equal("rice");

    [Theory]
    [InlineData("WHERE CAN I FIND RICE?")]
    [InlineData("WHERE DO THEY GROW RICE?")]
    [InlineData("WHERE DOES RICE GROW?")]
    [InlineData("WHERE IS RICE FOUND?")]
    [InlineData("WHICH COUNTRIES HAVE RICE?")]
    [InlineData("WHICH COUNTRIES IS RICE FOUND IN?")]
    public void ReadsEachShapeInAnyCase(string question) =>
        PlantRangeQuestion.NamesAskedAbout(question).Should().NotBeEmpty();

    [Theory]
    [InlineData("can I find rice?")]
    [InlineData("do they grow rice?")]
    [InlineData("does rice grow?")]
    [InlineData("is rice found?")]
    [InlineData("is rice found in Peru or Chile?")]
    [InlineData("countries is rice found?")]
    public void NeedsTheWhereabouts_NotJustTheVerb(string question) =>
        PlantRangeQuestion.NamesAskedAbout(question).Should().BeEmpty();
}
