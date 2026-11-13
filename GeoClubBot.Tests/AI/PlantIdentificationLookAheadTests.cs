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
/// The screenshot's plant, named before the answer: offered as the first excerpts when the service
/// can name it, as a notice when the day's identifications are spent, and never as a failure of the question.
/// </summary>
public sealed class PlantIdentificationLookAheadTests
{
    private const string Screenshot = "https://cdn.discordapp.com/attachments/1/2/shot.png?ex=abc";

    private readonly IPlantIdentifier _identifier = Substitute.For<IPlantIdentifier>();
    private readonly ISpeciesRangeReader _reader = Substitute.For<ISpeciesRangeReader>();
    private readonly ISpeciesRangeMap _map = Substitute.For<ISpeciesRangeMap>();
    private readonly AiToolsConfiguration _tools = new();

    public PlantIdentificationLookAheadTests()
    {
        _identifier.IsAvailable.Returns(true);
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound("ai.species_not_found", "No such plant."));
    }

    [Fact]
    public async Task OffersTheIdentification_ThenWhereThePlantGrows_WithItsMap()
    {
        Identifies(Candidate("Pinus kesiya", 0.61), Candidate("Pinus roxburghii", 0.2));
        var range = KhasiPine();
        _reader.ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null), Arg.Any<CancellationToken>()).Returns(range);
        _map.DrawAsync(range, Arg.Any<CancellationToken>()).Returns("https://bot.example/api/v1/ai/images/map.jpg");

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer.Should().NotBeNull();
        offer!.Notice.Should().BeNull();
        offer.Hits.Should().HaveCount(2);
        offer.Hits[0].Text.Should().StartWith("Pl@ntNet identifies the plant in the screenshot as Pinus kesiya");
        offer.Hits[1].SourceUrl.Should().Be("https://www.gbif.org/species/5285824");
        offer.Hits[1].ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        await _identifier.Received(1).IdentifyAsync(Screenshot, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffersOnlyTheGuess_WhenTheBestMatchIsWeak()
    {
        Identifies(Candidate("Pinus kesiya", 0.1));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().ContainSingle().Which.Text.Should().Contain("not sure");
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task OffersTheIdentificationAlone_WhenGbifDoesNotKnowThePlant()
    {
        Identifies(Candidate("Pinus kesiya", 0.61));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().ContainSingle();
        offer.Range.Should().BeNull();
    }

    [Fact]
    public async Task OffersTheRange_WithoutAMap_WhenDrawingFails()
    {
        Identifies(Candidate("Pinus kesiya", 0.61));
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>()).Returns(KhasiPine());
        _map.DrawAsync(Arg.Any<SpeciesRange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Range.Should().NotBeNull();
        offer.Range!.Kind.Should().Be(KnowledgeChunkKind.Text);
    }

    [Fact]
    public async Task TellsTheModelNothingWasRecognised_SoItDoesNotGuess()
    {
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound(PlantIdentificationErrorCodes.NotRecognised, "No plant."));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().ContainSingle().Which.Text.Should().Be(PlantIdentificationExcerpt.NotRecognisedText);
        offer.Notice.Should().BeNull();
    }

    [Fact]
    public async Task TellsTheMember_WhenTheDaysIdentificationsAreSpent()
    {
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Error.Conflict(PlantIdentificationErrorCodes.QuotaExceeded, "Spent."));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notice.Should().Be(PlantIdentificationLookAhead.QuotaNotice);
    }

    [Fact]
    public async Task TellsTheMember_WhenTheServiceCannotBeReached()
    {
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unexpected(PlantIdentificationErrorCodes.ServiceFailed, "Down."));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notice.Should().Be(PlantIdentificationLookAhead.UnavailableNotice);
    }

    [Fact]
    public async Task SaysNothing_WhenThePictureCouldNotBeSent()
    {
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, "webp"));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notice.Should().BeNull("the member can do nothing about the format");
    }

    [Fact]
    public async Task OffersNothing_AndNeverThrows_WhenTheServiceBlowsUp()
    {
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        var offer = await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notice.Should().Be(PlantIdentificationLookAhead.UnavailableNotice);
    }

    [Theory]
    [InlineData("what country is this?", Screenshot)]
    [InlineData("what tree is this?", null)]
    [InlineData("what tree is this?", "  ")]
    public async Task AsksTheServiceNothing_UnlessAPlantQuestionComesWithAPicture(string question, string? screenshot)
    {
        var offer = await LookAhead().ReadAsync(question, screenshot, CancellationToken.None);

        offer.Should().BeNull();
        await _identifier.DidNotReceiveWithAnyArgs().IdentifyAsync(default!, default);
    }

    [Fact]
    public async Task AsksTheServiceNothing_WhenItIsOffOrHasNoKey()
    {
        _identifier.IsAvailable.Returns(false);

        (await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None)).Should().BeNull();

        _identifier.IsAvailable.Returns(true);
        _tools.PlantIdentification.Enabled = false;

        (await LookAhead().ReadAsync("what tree is this?", Screenshot, CancellationToken.None)).Should().BeNull();
        await _identifier.DidNotReceiveWithAnyArgs().IdentifyAsync(default!, default);
    }

    private PlantIdentificationLookAhead LookAhead() =>
        new(_identifier, _reader, _map, Options.Create(_tools), TimeProvider.System, NullLogger<PlantIdentificationLookAhead>.Instance);

    private void Identifies(params PlantCandidate[] candidates) =>
        _identifier.IdentifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<PlantIdentification>.Success(new PlantIdentification(candidates, 480)));

    private static PlantCandidate Candidate(string name, double score) =>
        new(name, null, name.Split(' ')[0], "Pinaceae", ["Khasi pine"], score, null);

    private static SpeciesRange KhasiPine() =>
        new(5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: false, FuzzyMatch: false, null,
            [new SpeciesRangeArea("PH", 256), new SpeciesRangeArea("TH", 87)], 1061);
}
