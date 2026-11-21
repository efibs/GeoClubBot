using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Pictures to rate are drawn a random country at a time (#491): one guide held so many pictures that 55
/// drawn at random in a row were all from it.
/// </summary>
public sealed class QuizPictureDrawTests
{
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IAiQuizItemRepository _items = Substitute.For<IAiQuizItemRepository>();
    private readonly List<KnowledgeHit> _sampled = [];

    public QuizPictureDrawTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["chile", "estonia", "usa", "united states"]));
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var country = call.ArgAt<KnowledgeSample>(0).Country;
                var picture = QuizRounds.Picture($"A clue long enough to count as one, from {country}, a picture of its own.", country);
                _sampled.Add(picture);
                return Task.FromResult<IReadOnlyList<KnowledgeHit>>([picture]);
            });
        _items.ReadContentHashesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>()));
    }

    [Fact]
    public async Task TakesOnePicturePerCountry_EverySpellingOfACountryCountingAsOne()
    {
        var pictures = await Draw().DrawUnratedAsync(3, null, null, 10, CancellationToken.None);

        pictures.Select(picture => picture.Country == "usa" ? "united states" : picture.Country)
            .Should().OnlyHaveUniqueItems().And.BeEquivalentTo("chile", "estonia", "united states");
    }

    [Fact]
    public async Task KeepsToTheCountryAsked_AndLeavesOutTheGuideFamilyAsked()
    {
        await Draw().DrawUnratedAsync(1, ["estonia"], "plonkit", 3, CancellationToken.None);

        await _index.Received().SampleAsync(
            Arg.Is<KnowledgeSample>(sample => sample.Country == "estonia" && sample.ExcludedSourceType == "plonkit" && sample.Kind == KnowledgeChunkKind.Image),
            Arg.Any<CancellationToken>());
        await _index.DidNotReceive().SampleAsync(Arg.Is<KnowledgeSample>(sample => sample.Country == "chile"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopsWhenNothingUnratedIsLeft()
    {
        _items.ReadContentHashesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                _sampled.Where(hit => call.ArgAt<IReadOnlyCollection<Guid>>(0).Contains(hit.Id))
                    .ToDictionary(hit => hit.Id, QuizPictureRater.ContentHashOf)));

        (await Draw().DrawUnratedAsync(5, null, null, 1000, CancellationToken.None)).Should().HaveCount(0);
        // One round over the three countries, the United States under both its spellings, then it gives up.
        await _index.ReceivedWithAnyArgs(4).SampleAsync(default!, default);
    }

    private QuizPictureDraw Draw()
    {
        var countries = new KnowledgeCountryDirectory(_index, Options.Create(new AiRetrievalConfiguration()), TimeProvider.System,
            NullLogger<KnowledgeCountryDirectory>.Instance);
        var rater = new QuizPictureRater(Substitute.For<IChatModelClient>(), Substitute.For<IChatModelCatalog>(), Substitute.For<IImageRelay>(),
            Substitute.For<IAiBudgetRepository>(), _items, Options.Create(new AiConfiguration()), Options.Create(new AiQuizConfiguration()),
            TimeProvider.System, NullLogger<QuizPictureRater>.Instance);
        return new QuizPictureDraw(_index, _items, countries, rater);
    }
}
