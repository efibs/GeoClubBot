using FluentAssertions;
using UseCases.UseCases.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class ChunkTopicClassifierTests
{
    [Theory]
    [InlineData("Bollards here are short and white with a reflective band.", "Infrastructure")]
    [InlineData("Utility poles are concrete with two crossarms.", "Infrastructure")]
    [InlineData("Olive groves dominate the centre, planted in wide grids.", "Agriculture")]
    [InlineData("Flat white roofs with a low parapet are the norm.", "Architecture")]
    [InlineData("The script is Arabic script with distinctive diacritics.", "Language and script")]
    [InlineData("Licence plates are black with white text.", "Vehicles and plates")]
    [InlineData("Area codes narrow you down to a governorate.", "Regional clues")]
    [InlineData("A gen 3 car with a roof rack appears in the south.", "Google car and coverage")]
    [InlineData("Conifer species change with altitude in the north.", "Landscape and vegetation")]
    public void Classify_LabelsTheKindOfClueDescribed(string text, string expected) =>
        ChunkTopicClassifier.Classify(text).Should().Be(expected);

    [Fact]
    public void Classify_PrefersTheMoreSpecificTopic()
    {
        // A pole standing in a field is an infrastructure clue, not an agricultural one.
        ChunkTopicClassifier.Classify("The utility pole stands at the edge of a wheat field.")
            .Should().Be("Infrastructure");
    }

    [Fact]
    public void Classify_ReturnsNothingRatherThanAMeaninglessLabel()
    {
        // Measured: non-topical text in the embedding header lowers similarity, so a catch-all
        // category would be worse than no category.
        ChunkTopicClassifier.Classify("Thanks to everyone who helped put this together.")
            .Should().BeNull();
    }

    [Fact]
    public void Classify_IgnoresCasing() =>
        ChunkTopicClassifier.Classify("BOLLARDS ARE SHORT").Should().Be("Infrastructure");

    [Fact]
    public void Classify_HandlesEmptyText() => ChunkTopicClassifier.Classify("   ").Should().BeNull();
}
