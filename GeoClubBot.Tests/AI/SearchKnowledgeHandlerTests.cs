using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class SearchKnowledgeHandlerTests
{
    private readonly IEmbedder _embedder = Substitute.For<IEmbedder>();
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();

    private SearchKnowledgeHandler CreateHandler() => new(_embedder, _index);

    private void EmbeddingGives(float[] vector) =>
        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReadOnlyMemory<float>>>.Success([vector]));

    [Fact]
    public async Task SearchesTheIndexWithTheQuestionsVector_AndTheRequestedFilters()
    {
        var vector = new[] { 0.25f, 0.75f };
        EmbeddingGives(vector);
        var hit = new KnowledgeHit(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Text, "bollards", "https://x", null, null, null, null, null, 0);
        _index.SearchAsync(Arg.Any<KnowledgeQuery>(), Arg.Any<CancellationToken>()).Returns([hit]);

        var result = await CreateHandler().Handle(new SearchKnowledgeQuery("bollards", "Germany", 3), CancellationToken.None);

        result.Value.Should().Equal(hit);
        await _embedder.Received(1).EmbedAsync(
            Arg.Is<IReadOnlyList<EmbeddingInput>>(inputs => inputs.SequenceEqual(new[] { new TextEmbeddingInput("bollards") })),
            Arg.Any<CancellationToken>());
        await _index.Received(1).SearchAsync(
            Arg.Is<KnowledgeQuery>(q =>
                q.TextVector!.Value.ToArray().SequenceEqual(vector)
                && q.Country == "Germany"
                && q.Limit == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedEmbedding_IsReturned_WithoutSearching()
    {
        var error = Error.Unexpected(EmbeddingErrorCodes.Failed, "no provider");
        _embedder.EmbedAsync(Arg.Any<IReadOnlyList<EmbeddingInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReadOnlyMemory<float>>>.Failure(error));

        var result = await CreateHandler().Handle(new SearchKnowledgeQuery("bollards"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        await _index.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }
}
