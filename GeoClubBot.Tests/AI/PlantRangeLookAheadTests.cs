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

        var hit = await LookAhead().ReadAsync("where can I find Khasi pines?", CancellationToken.None);

        hit.Should().NotBeNull();
        hit!.SourceUrl.Should().Be("https://www.gbif.org/species/5285824");
        hit.Text.Should().Contain("Philippines").And.Contain("Thailand").And.Contain("Found by its common name \"Khasi pine\"");
        await _reader.Received(1).ReadAsync(new SpeciesRangeQuery("Khasi pines", null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AsksNothing_OfAQuestionAboutOneCountry()
    {
        var hit = await LookAhead().ReadAsync("where can I find sugar cane in the Philippines?", CancellationToken.None);

        hit.Should().BeNull();
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
        hit!.SectionPath.Should().Be("Where Eucalyptus is recorded (GBIF)");
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
        new(_reader, Options.Create(_tools), TimeProvider.System, NullLogger<PlantRangeLookAhead>.Instance);

    private void Knows(string name, SpeciesRange range) =>
        _reader.ReadAsync(new SpeciesRangeQuery(name, null), Arg.Any<CancellationToken>()).Returns(range);

    private static SpeciesRange KhasiPine(bool byCommonName) => new(
        5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", byCommonName, FuzzyMatch: false, CountryCode: null,
        [new("PH", 256), new("CN", 105), new("TH", 87), new("IN", 80), new("VN", 66), new("MM", 60)], 1061);

    private static SpeciesRange Range(string name, bool byCommonName, long records) => new(
        1, name, name, byCommonName ? "SPECIES" : "GENUS", byCommonName, FuzzyMatch: false, CountryCode: null,
        [new("AU", records)], records);
}
