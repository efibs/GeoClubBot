using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>A quiz question: a captioned picture drawn at random, within the country asked, and the answer the guide gives.</summary>
public sealed class DrawQuizQuestionHandlerTests
{
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly AiQuizConfiguration _settings = new() { Draws = 10 };
    private readonly AiConfiguration _ai = new() { Active = true };

    public DrawQuizQuestionHandlerTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "usa", "united states"]));
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([]));
    }

    [Fact]
    public async Task DrawsACaptionedPicture_AndReadsTheAnswerOffTheGuide()
    {
        var clue = Picture("In Lạng Sơn the poles carry a white band at eye height, seen nowhere else in the north.", "vietnam");
        var heading = Picture("Vietnam", "vietnam");
        Samples(heading, clue);

        var result = await Handler().Handle(new DrawQuizQuestionQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Clue.Should().Be(clue, "a picture under a bare heading makes no question");
        result.Value.CountryName.Should().Be("Vietnam");
        result.Value.Guide.Should().Be("Vietnam region guide");
        result.Value.Section.Should().Be("Poles");
        result.Value.Answer.Should().StartWith("In Lạng Sơn the poles carry a white band");

        await _index.Received(1).SampleAsync(
            Arg.Is<KnowledgeSample>(sample => sample.Limit == 10 && sample.Kind == KnowledgeChunkKind.Image && sample.ExcludedSourceType == null && sample.Country == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrawsWithinTheCountryAsked_UnderEverySpellingTheIndexUses()
    {
        var clue = Picture("A clue long enough to count as one, about the United States' poles.", "united states");
        _index.SampleAsync(Arg.Is<KnowledgeSample>(sample => sample.Country == "usa"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([Picture("USA", "usa")]));
        _index.SampleAsync(Arg.Is<KnowledgeSample>(sample => sample.Country == "united states"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>([clue]));

        var result = await Handler().Handle(new DrawQuizQuestionQuery("US"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Clue.Should().Be(clue);
    }

    [Fact]
    public async Task LeavesPlonkItOut_WhenOnlyLesserKnownCluesAreWanted()
    {
        Samples(Picture("A clue long enough to count as one, about a bollard and its band.", "vietnam"));

        var result = await Handler().Handle(new DrawQuizQuestionQuery(LesserKnownOnly: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _index.Received(1).SampleAsync(
            Arg.Is<KnowledgeSample>(sample => sample.ExcludedSourceType == MainGuideOverlap.MainGuide),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Atlantis", "No indexed guide covers")]
    [InlineData("Scandinavia", "is several countries")]
    public async Task RefusesACountryItCannotPlace(string country, string reason)
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "sweden", "norway", "denmark"]));

        var result = await Handler().Handle(new DrawQuizQuestionQuery(country), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(QuizErrorCodes.UnknownCountry);
        result.Error.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task SaysSo_WhenNothingCaptionedWasDrawn()
    {
        Samples(Picture("Vietnam", "vietnam"));

        var result = await Handler().Handle(new DrawQuizQuestionQuery(), CancellationToken.None);

        result.Error.Code.Should().Be(QuizErrorCodes.NothingFound);
    }

    [Fact]
    public async Task IsOff_WhenTurnedOff()
    {
        _settings.Enabled = false;

        (await Handler().Handle(new DrawQuizQuestionQuery(), CancellationToken.None)).Error.Code.Should().Be(QuizErrorCodes.Disabled);
        await _index.DidNotReceiveWithAnyArgs().SampleAsync(default!, default);
    }

    private DrawQuizQuestionHandler Handler()
    {
        var retrieval = Options.Create(new AiRetrievalConfiguration());
        var countries = new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);
        var overlap = new MainGuideOverlap(_index, countries, retrieval, NullLogger<MainGuideOverlap>.Instance);

        return new DrawQuizQuestionHandler(_index, overlap, countries, Options.Create(_settings), Options.Create(_ai), NullLogger<DrawQuizQuestionHandler>.Instance);
    }

    private void Samples(params KnowledgeHit[] hits) =>
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>(hits));

    private static KnowledgeHit Picture(string text, string country) =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, text, "https://docs.google.com/document/d/x#heading=h.1",
            $"https://bot.example/api/v1/ai/images/{Guid.NewGuid():N}.jpg", "Vietnam region guide", country,
            "Vietnam region guide > Poles", "Author", 0, "gdoc", "x", "p3-img");
}
