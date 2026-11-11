using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The plant lookup before an answer: it offers only a plant it can stand behind, and never costs the
/// question anything when it cannot.
/// </summary>
public sealed class PlantRangeLookAheadTests
{
    private readonly ISpeciesRangeReader _reader = Substitute.For<ISpeciesRangeReader>();
    private readonly ISpeciesRangeMap _map = Substitute.For<ISpeciesRangeMap>();
    private readonly AiToolsConfiguration _tools = new();

    public PlantRangeLookAheadTests()
    {
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound("ai.species_not_found", "No such plant."));
    }

    [Fact]
    public async Task OffersTheCountries_OfThePlantAQuestionAsksTheWhereaboutsOf()
    {
        // GBIF knows "khasi pine", not "khasi pines": the singular is asked next.
        Knows("Khasi pine", KhasiPine(byCommonName: true));

        var offer = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        offer.Should().NotBeNull();
        offer!.LeadsTheGuides.Should().BeTrue("a plant's countries answer the question's where");
        var hit = offer.Hit;
        hit.SourceUrl.Should().Be("https://www.gbif.org/species/5285824");
        hit.Text.Should().Contain("Philippines").And.Contain("Thailand").And.Contain("Found by its common name \"Khasi pine\"");
        hit.Kind.Should().Be(KnowledgeChunkKind.Text, "no map was drawn");
        await _reader.Received(1).ReadAsync(new SpeciesRangeQuery("Khasi pines", null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffersTheMapOfThePlantsRecords_AsTheExcerptsPicture()
    {
        // "Please show the GBIF distribution": the countries were offered, the map was what was asked for.
        var khasiPine = KhasiPine(byCommonName: true);
        Knows("Khasi pine", khasiPine);
        _map.DrawAsync(khasiPine, Arg.Any<CancellationToken>()).Returns("https://bot.example/api/v1/ai/images/map.jpg");

        var offer = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        offer!.Hit.Kind.Should().Be(KnowledgeChunkKind.Image);
        offer.Hit.ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        offer.Hit.Text.Should().Contain("The picture is a map of these records");
    }

    [Fact]
    public async Task OffersThePlantsRange_WhenTheMapCannotBeDrawn()
    {
        Knows("Khasi pine", KhasiPine(byCommonName: true));
        _map.DrawAsync(Arg.Any<SpeciesRange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        var offer = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        offer!.Hit.Kind.Should().Be(KnowledgeChunkKind.Text);
        offer.Hit.Text.Should().Contain("Philippines");
    }

    [Theory]
    [InlineData("where in the us can you see cholla?", "cholla", "US")]
    [InlineData("where in Brazil can you see Moriche palms", "Moriche palm", "BR")]
    [InlineData("where can I find tree yucca in Mexico?", "tree yucca", "MX")]
    public async Task CountsThePlantWithinTheCountryAQuestionNames_AndLetsTheGuidesLead(string question, string name, string code)
    {
        // "I would have liked a distribution map" (where in the us can you see cholla?). Such a question
        // was not looked up at all: the country stood between "where" and the verb.
        var inCountry = new SpeciesRange(
            7628502, "Cylindropuntia (Engelm.) F.M.Knuth", "Cylindropuntia", "GENUS", MatchedByCommonName: false, FuzzyMatch: false,
            code, [new("Arizona", 21_000), new("California", 9_000)], 41_000);
        _reader.ReadAsync(new SpeciesRangeQuery(name, code), Arg.Any<CancellationToken>()).Returns(inCountry);
        _map.DrawAsync(inCountry, Arg.Any<CancellationToken>()).Returns("https://bot.example/api/v1/ai/images/us.jpg");

        var offer = await LookAhead().ReadAsync(question, CancellationToken.None);

        offer.Should().NotBeNull();
        offer!.LeadsTheGuides.Should().BeFalse("within a country the guides say what is seen; the records add a map");
        offer.Hit.ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/us.jpg");
        offer.Hit.Text.Should().Contain("Arizona 21,000");
    }

    [Fact]
    public async Task OffersNothing_OfAPlantTheDatabaseHardlyKnowsInThatCountry()
    {
        // 308 records of sugar cane in the Philippines, none of the top eight provinces in Negros, which
        // grows most of it: that is where somebody looked, not where it grows.
        _reader.ReadAsync(new SpeciesRangeQuery("sugar cane", "PH"), Arg.Any<CancellationToken>())
            .Returns(new SpeciesRange(1, "Saccharum officinarum L.", "Saccharum officinarum", "SPECIES", true, false, "PH",
                [new("Laguna", 21)], 308));

        var offer = await LookAhead().ReadAsync("where can I find sugar cane in the Philippines?", CancellationToken.None);

        offer.Should().BeNull();
        await _map.DidNotReceiveWithAnyArgs().DrawAsync(default!, default);
    }

    [Theory]
    [InlineData("where can I find sugar cane in my area?")]
    [InlineData("where in the Sonoran desert can you see saguaros?")]
    public async Task AsksNothing_OfAQuestionAboutAPlaceThatIsNoCountry(string question)
    {
        var offer = await LookAhead().ReadAsync(question, CancellationToken.None);

        offer.Should().BeNull();
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task NeverTakesALoneWordLeftByDroppingTree_ForACommonName()
    {
        // "dragon" is a common name of tarragon.
        Knows("dragon", Range("Artemisia dracunculus", byCommonName: true, records: 19_632));

        var hit = await LookAhead().ReadAsync("where can I find dragon trees?", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task TakesALoneWordLeftByDroppingTree_ForAGenus()
    {
        Knows("eucalyptus", Range("Eucalyptus", byCommonName: false, records: 1_952_884));

        var hit = await LookAhead().ReadAsync("where are eucalyptus trees common?", CancellationToken.None);

        hit.Should().NotBeNull();
        hit!.Hit.SectionPath.Should().Be("Where Eucalyptus is recorded (GBIF)");
    }

    [Fact]
    public async Task OffersNothing_ForATaxonWithAHandfulOfRecords()
    {
        Knows("baobabs", Range("Baobabia", byCommonName: false, records: 4));

        var hit = await LookAhead().ReadAsync("where can I find baobabs?", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task StopsAsking_WhenGbifCannotBeRead()
    {
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unexpected("ai.species_range_unavailable", "Down."));

        var hit = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        hit.Should().BeNull();
        await _reader.ReceivedWithAnyArgs(1).ReadAsync(default!, default);
    }

    [Fact]
    public async Task OffersNothing_AndThrowsNothing_WhenTheReadIsCancelledOrThrows()
    {
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        (await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None)).Should().BeNull();

        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("A bug."));

        (await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task LetsTheQuestionsOwnCancellationThrough()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var read = () => LookAhead().ReadAsync("where can I find Khasi pines?", cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AsksNothing_WhenTurnedOff(bool enabled, bool lookUpWhereQuestions)
    {
        _tools.SpeciesRange.Enabled = enabled;
        _tools.SpeciesRange.LookUpWhereQuestions = lookUpWhereQuestions;
        Knows("Khasi pine", KhasiPine(byCommonName: true));

        var hit = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        hit.Should().BeNull();
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    private PlantRangeLookAhead LookAhead() =>
        new(_reader, _map, Options.Create(_tools), TimeProvider.System, NullLogger<PlantRangeLookAhead>.Instance);

    private void Knows(string name, SpeciesRange range) =>
        _reader.ReadAsync(new SpeciesRangeQuery(name, null), Arg.Any<CancellationToken>()).Returns(range);

    private static SpeciesRange KhasiPine(bool byCommonName) => new(
        5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", byCommonName, FuzzyMatch: false, CountryCode: null,
        [new("PH", 256), new("CN", 105), new("TH", 87), new("IN", 80), new("VN", 66), new("MM", 60)], 1061);

    private static SpeciesRange Range(string name, bool byCommonName, long records) => new(
        1, name, name, byCommonName ? "SPECIES" : "GENUS", byCommonName, FuzzyMatch: false, CountryCode: null,
        [new("AU", records)], records);
}
