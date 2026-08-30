using FluentAssertions;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What goes into a vector decides what can be found. Measured against the embedding model, a topical
/// header is the largest single influence on retrieval — and non-topical filler costs almost as much
/// as the topic gains, which is why the exclusions below matter as much as the inclusions.
/// </summary>
public sealed class EmbeddingTextBuilderTests
{
    private const string Poles =
        "Most are concrete with a slight taper, and you often see two crossarms near the top.";

    [Fact]
    public void Build_LeadsWithTheCountry_SoAPassageThatNeverNamesItIsStillFindable()
    {
        // Guide prose says "the bollards here", never "Tunisian bollards"; without the country in the
        // vector, a question naming the country cannot reach it.
        var text = Build(Chunk(Poles, "Tunisia > Identifying Tunisia"), country: "Tunisia");

        text.Should().StartWith("Tunisia — ");
        text.Should().EndWith(Poles, "the stored and displayed text stays bare");
    }

    [Fact]
    public void Build_KeepsTheSourcesOwnHeading()
    {
        // A source's real heading is the best topic available and beats anything guessed.
        var text = Build(Chunk(Poles, "Tunisia > Identifying Tunisia > Poles and wires"), country: "Tunisia");

        text.Should().Contain("Identifying Tunisia > Poles and wires");
    }

    [Fact]
    public void Build_AddsATopic_WhenTheSourceHasNoUsefulStructure()
    {
        // The slide-deck and album case, which is most of the library.
        var text = Build(Chunk(Poles, "Tunisia Meta Deck > Slide 4"), country: "Tunisia", title: "Tunisia Meta Deck");

        text.Should().Contain("Infrastructure");
    }

    [Fact]
    public void Build_DropsASlideNumber()
    {
        // Position, not subject. Measured to pull similarity back down to roughly the level of no
        // header at all.
        var text = Build(Chunk(Poles, "Tunisia Meta Deck > Slide 4"), country: "Tunisia", title: "Tunisia Meta Deck");

        text.Should().NotContain("Slide 4");
    }

    [Fact]
    public void Build_DropsTheSourcesOwnTitle()
    {
        // Repeated on every chunk of the source, so it separates nothing — and measured to cost more
        // than it gives.
        var text = Build(Chunk(Poles, "Tunisia Meta Deck > Slide 4"), country: "Tunisia", title: "Tunisia Meta Deck");

        text.Should().NotContain("Tunisia Meta Deck");
    }

    [Fact]
    public void Build_DropsAnAuthorCreditMistakenForAHeading()
    {
        // The document heading heuristic picks up bylines; they label nothing.
        var text = Build(Chunk(Poles, "Driving Directions > @bagaboiebailey"),
            country: "Tunisia", title: "Driving Directions");

        text.Should().NotContain("@bagaboiebailey");
    }

    [Fact]
    public void Build_DropsAParagraphMisreadAsAHeading()
    {
        var longHeading = new string('x', 120);

        Build(Chunk(Poles, $"Doc > {longHeading}"), country: "Tunisia", title: "Doc")
            .Should().NotContain(longHeading);
    }

    [Fact]
    public void Build_DoesNotRepeatATopicTheHeadingAlreadyStates()
    {
        // "Architecture — Architecture > Roofs" is duplication, and every extra word dilutes.
        var text = Build(
            Chunk("Flat white roofs with a low parapet are the norm.", "Tunisia > Architecture > Roofs"),
            country: "Tunisia");

        text.Split("Architecture").Length.Should().Be(2, "the topic appears exactly once");
    }

    [Fact]
    public void Build_OmitsTheTopicEntirely_WhenNothingMatches()
    {
        // A meaningless label would be filler, and filler measurably lowers similarity — so saying
        // nothing is better than saying "Miscellaneous".
        var text = Build(Chunk("Some prose with no recognisable subject at all.", "Doc > Slide 1"),
            country: "Tunisia", title: "Doc");

        text.Should().Be("Tunisia\n\nSome prose with no recognisable subject at all.");
    }

    [Fact]
    public void Build_FallsBackToTheBareChunk_WhenThereIsNothingToSayAboutIt()
    {
        var text = EmbeddingTextBuilder.Build(
            new SourceDescriptor("gdoc", "k", new Uri("https://example.invalid/d")),
            new ExtractedChunk("c0", "Slide 2", "Unclassifiable prose."));

        text.Should().Be("Unclassifiable prose.");
    }

    private static string Build(ExtractedChunk chunk, string? country = null, string? title = null) =>
        EmbeddingTextBuilder.Build(
            new SourceDescriptor("gdoc", "key", new Uri("https://example.invalid/d"), title, country),
            chunk);

    private static ExtractedChunk Chunk(string text, string sectionPath) => new("c0", sectionPath, text);
}
