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
    private readonly IPlantRecordCounter _records = Substitute.For<IPlantRecordCounter>();
    private readonly IPlaceDirectory _places = Substitute.For<IPlaceDirectory>();
    private readonly ISpeciesRangeReader _reader = Substitute.For<ISpeciesRangeReader>();
    private readonly ISpeciesRangeMap _map = Substitute.For<ISpeciesRangeMap>();
    private readonly AiToolsConfiguration _tools = new();

    public PlantIdentificationLookAheadTests()
    {
        _identifier.IsAvailable.Returns(true);
        _places.ReadAsync(Arg.Any<CancellationToken>()).Returns(PlaceFixtures.All);
        _records.CountAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<RecordArea>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unexpected("ai.plant_records_unavailable", "Not set up."));
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

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer.Should().NotBeNull();
        offer!.Notices.Should().BeEmpty();
        offer.Hits.Should().HaveCount(2);
        offer.Hits[0].Text.Should().StartWith("Pl@ntNet, an image-based plant identification service, identifies the plant in the screenshot as Pinus kesiya");
        offer.Hits[1].SourceUrl.Should().Be("https://www.gbif.org/species/5285824");
        offer.Hits[1].ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");
        await _identifier.Received(1).IdentifyAsync(Arg.Is<IReadOnlyList<string>>(pictures => pictures.SequenceEqual(new[] { Screenshot })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffersOnlyTheGuess_WhenTheBestMatchIsWeak()
    {
        Identifies(Candidate("Pinus kesiya", 0.1));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().ContainSingle().Which.Text.Should().Contain("not sure");
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.CropTipNotice, PlantIdentificationLookAhead.PlaceTipNotice);
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task OffersTheIdentificationAlone_WhenGbifDoesNotKnowThePlant()
    {
        Identifies(Candidate("Pinus kesiya", 0.61));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().ContainSingle();
        offer.Range.Should().BeNull();
    }

    [Fact]
    public async Task OffersTheRange_WithoutAMap_WhenDrawingFails()
    {
        Identifies(Candidate("Pinus kesiya", 0.61));
        _reader.ReadAsync(Arg.Any<SpeciesRangeQuery>(), Arg.Any<CancellationToken>()).Returns(KhasiPine());
        _map.DrawAsync(Arg.Any<SpeciesRange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Range.Should().NotBeNull();
        offer.Range!.Kind.Should().Be(KnowledgeChunkKind.Text);
    }

    [Fact]
    public async Task TellsTheModelNothingWasRecognised_SoItDoesNotGuess()
    {
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound(PlantIdentificationErrorCodes.NotRecognised, "No plant."));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().ContainSingle().Which.Text.Should().Be(PlantIdentificationExcerpt.NotRecognisedText);
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.CropTipNotice);
    }

    [Fact]
    public async Task TellsTheMember_WhenTheDaysIdentificationsAreSpent()
    {
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Error.Conflict(PlantIdentificationErrorCodes.QuotaExceeded, "Spent."));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.QuotaNotice);
    }

    [Fact]
    public async Task TellsTheMember_WhenTheServiceCannotBeReached()
    {
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unexpected(PlantIdentificationErrorCodes.ServiceFailed, "Down."));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.UnavailableNotice);
    }

    [Fact]
    public async Task SaysNothing_WhenThePictureCouldNotBeSent()
    {
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(PlantIdentificationErrorCodes.ImageUnusable, "webp"));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notices.Should().BeEmpty("the member can do nothing about the format");
    }

    [Fact]
    public async Task OffersNothing_AndNeverThrows_WhenTheServiceBlowsUp()
    {
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None);

        offer!.Hits.Should().BeEmpty();
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.UnavailableNotice);
    }

    [Theory]
    [InlineData("what country is this?", true)]
    [InlineData("what tree is this?", false)]
    public async Task AsksTheServiceNothing_UnlessAPlantQuestionComesWithAPicture(string question, bool withPicture)
    {
        var offer = await LookAhead().ReadAsync(question, withPicture ? [Screenshot] : [], [], CancellationToken.None);

        offer.Should().BeNull();
        await _identifier.DidNotReceiveWithAnyArgs().IdentifyAsync(default!, default);
    }

    [Fact]
    public async Task SendsEveryPictureOfTheQuestion_UpToTheLimit()
    {
        Identifies(Candidate("Pinus kesiya", 0.61));
        _tools.PlantIdentification.MaxPictures = 2;

        await LookAhead().ReadAsync("what tree is this?", ["https://a/1.png", "https://a/2.png", "https://a/3.png"], [], CancellationToken.None);

        await _identifier.Received(1).IdentifyAsync(
            Arg.Is<IReadOnlyList<string>>(pictures => pictures.SequenceEqual(new[] { "https://a/1.png", "https://a/2.png" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WeighsTheCandidates_ByTheRecordsWhereTheMemberSaidItWasTaken()
    {
        // The hazy white pine of #485: the picture's favourite is American elm at 1 %.
        Identifies(Candidate("Ulmus americana", 0.013, 1), Candidate("Acer rubrum", 0.013, 2), Candidate("Pinus strobus", 0.010, 3));
        _records.CountAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CircleRecordArea>(), Arg.Any<CancellationToken>())
            .Returns(Counts((1, 209), (2, 2_182), (3, 7_267)));
        _records.CountAsync(Arg.Any<IReadOnlyCollection<long>>(), new CountryRecordArea("US"), Arg.Any<CancellationToken>())
            .Returns(Counts((1, 45_216), (2, 131_138), (3, 81_318)));

        var offer = await LookAhead().ReadAsync("what tree is this? it's just south of Boston", [Screenshot], [], CancellationToken.None);

        var text = offer!.Identification!.Text;
        text.Should().Contain("\"south of Boston\" (Boston, Massachusetts, United States)")
            .And.Contain("Likeliest there: Pinus strobus");
        offer.Range.Should().BeNull("the picture alone gives white pine 1 %: a likelihood, not an identification");
        offer.Notices.Should().Equal(
            "Plant candidates weighed by GBIF records within 50 km of Boston (Boston, Massachusetts, United States).",
            PlantIdentificationLookAhead.CropTipNotice);

        await _records.Received(1).CountAsync(
            Arg.Is<IReadOnlyCollection<long>>(keys => keys.Order().SequenceEqual(new long[] { 1, 2, 3 })),
            Arg.Is<CircleRecordArea>(area => area.RadiusKm == 50 && Math.Abs(area.Latitude - 42.358) < 0.01),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountsACountryOnce_WhenTheCountryIsThePlace()
    {
        Identifies(Candidate("Pinus resinosa", 0.126, 1), Candidate("Pinus nigra", 0.038, 3));
        _records.CountAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<RecordArea>(), Arg.Any<CancellationToken>()).Returns(Counts((3, 33)));
        _tools.PlantIdentification.MaxCandidates = 1;

        var offer = await LookAhead().ReadAsync("what tree is this? It's in Bulgaria", [Screenshot], [], CancellationToken.None);

        offer!.Identification!.Text.Should().Contain("Pinus resinosa (Khasi pine; family Pinaceae) (13 %), has no GBIF record in Bulgaria");
        offer.Notices[0].Should().Be("Plant candidates weighed by GBIF records in Bulgaria.");
        await _records.Received(1).CountAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<RecordArea>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffersPlNetsOrder_WhenTheRecordsCannotBeRead()
    {
        Identifies(Candidate("Pinus kesiya", 0.61, 1));

        var offer = await LookAhead().ReadAsync("what tree is this? It's in Bulgaria", [Screenshot], [], CancellationToken.None);

        offer!.Identification!.Text.Should().Contain("GBIF's records there could not be read");
        offer.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task WeighsAgain_WhenAFollowUpSaysWhereTheScreenshotWasTaken()
    {
        Identifies(Candidate("Pinus kesiya", 0.61, 1));

        var offer = await LookAhead().ReadAsync("it's near Boston though", [Screenshot], ["what tree is this?"], CancellationToken.None);

        offer.Should().NotBeNull();
        offer!.Identification!.Text.Should().Contain("Boston");
    }

    [Fact]
    public async Task AsksNothing_ForAFollowUpThatSaysNoPlace_OrABranchThatNeverAskedAboutAPlant()
    {
        (await LookAhead().ReadAsync("and where does it grow?", [Screenshot], ["what tree is this?"], CancellationToken.None)).Should().BeNull();
        (await LookAhead().ReadAsync("it's near Boston", [Screenshot], ["which country is this?"], CancellationToken.None)).Should().BeNull();
        (await LookAhead().ReadAsync("what about the bollards in Bulgaria?", [Screenshot], ["what tree is this?"], CancellationToken.None)).Should().BeNull();

        await _identifier.DidNotReceiveWithAnyArgs().IdentifyAsync(default!, default);
    }

    [Fact]
    public async Task ReadsWhereTheScreenshotWasTaken_FromEarlierInTheBranch()
    {
        Identifies(Candidate("Pinus kesiya", 0.61, 1));

        var offer = await LookAhead().ReadAsync("what tree is this?", [Screenshot], ["This one is from Bulgaria"], CancellationToken.None);

        offer!.Identification!.Text.Should().Contain("\"from Bulgaria\" (Bulgaria)");
    }

    [Fact]
    public async Task IgnoresThePlace_WhenWeighingIsOff()
    {
        Identifies(Candidate("Pinus kesiya", 0.1, 1));
        _tools.PlantIdentification.Location.Enabled = false;

        var offer = await LookAhead().ReadAsync("what tree is this? It's in Bulgaria", [Screenshot], [], CancellationToken.None);

        offer!.Identification!.Text.Should().NotContain("Bulgaria");
        offer.Notices.Should().Equal(PlantIdentificationLookAhead.CropTipNotice);
        await _places.DidNotReceiveWithAnyArgs().ReadAsync(default);
    }

    [Fact]
    public async Task AsksTheServiceNothing_WhenItIsOffOrHasNoKey()
    {
        _identifier.IsAvailable.Returns(false);

        (await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None)).Should().BeNull();

        _identifier.IsAvailable.Returns(true);
        _tools.PlantIdentification.Enabled = false;

        (await LookAhead().ReadAsync("what tree is this?", [Screenshot], [], CancellationToken.None)).Should().BeNull();
        await _identifier.DidNotReceiveWithAnyArgs().IdentifyAsync(default!, default);
    }

    private PlantIdentificationLookAhead LookAhead() =>
        new(_identifier, _records, _reader, _map, new KnownPlaceDirectory(_places), Options.Create(_tools), TimeProvider.System,
            NullLogger<PlantIdentificationLookAhead>.Instance);

    private void Identifies(params PlantCandidate[] candidates) =>
        _identifier.IdentifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<PlantIdentification>.Success(new PlantIdentification(candidates, 480)));

    private static PlantCandidate Candidate(string name, double score, long? key = null) =>
        new(name, null, name.Split(' ')[0], "Pinaceae", ["Khasi pine"], score, key);

    private static Result<IReadOnlyDictionary<long, long>> Counts(params (long Key, long Count)[] counts) =>
        Result<IReadOnlyDictionary<long, long>>.Success(counts.ToDictionary(count => count.Key, count => count.Count));

    private static SpeciesRange KhasiPine() =>
        new(5285824, "Pinus kesiya Royle ex Gordon", "Pinus kesiya", "SPECIES", MatchedByCommonName: false, FuzzyMatch: false, null,
            [new SpeciesRangeArea("PH", 256), new SpeciesRangeArea("TH", 87)], 1061);
}
