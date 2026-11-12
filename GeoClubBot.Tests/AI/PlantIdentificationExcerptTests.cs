using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>What the model is told about the screenshot's plant, so it names it from the service rather than a look-alike guide picture.</summary>
public sealed class PlantIdentificationExcerptTests
{
    [Fact]
    public void ToHit_StatesTheIdentification_WithItsConfidence_AndTheRunnersUp()
    {
        var identification = new PlantIdentification(
        [
            new PlantCandidate("Pinus kesiya", "Royle ex Gordon", "Pinus", "Pinaceae", ["Khasi pine", "Benguet pine"], 0.612, 5285824),
            new PlantCandidate("Pinus roxburghii", "Sarg.", "Pinus", "Pinaceae", ["Chir pine"], 0.21, 5285750),
            new PlantCandidate("Pinus merkusii", null, "Pinus", "Pinaceae", [], 0.05, null)
        ], RemainingRequests: 480);

        var hit = PlantIdentificationExcerpt.ToHit(identification, minConfidence: 0.25, maxCandidates: 3);

        hit.Kind.Should().Be(KnowledgeChunkKind.Text);
        hit.Title.Should().Be("Pl@ntNet");
        hit.SectionPath.Should().Be("What plant the screenshot shows (Pl@ntNet)");
        hit.SourceUrl.Should().Be("https://identify.plantnet.org/");
        hit.Text.Should().Be(
            "Pl@ntNet identifies the plant in the screenshot as Pinus kesiya (Khasi pine, Benguet pine; family Pinaceae) with 61 % confidence. "
            + "Other candidates: Pinus roxburghii (Chir pine; family Pinaceae) (21 %); Pinus merkusii (family Pinaceae) (5 %). "
            + "The identification is by image recognition and can be wrong; name the plant with its scientific name and the confidence.");
    }

    [Fact]
    public void ToHit_CallsAWeakBestMatchAGuess()
    {
        var identification = new PlantIdentification(
            [new PlantCandidate("Yucca brevifolia", null, "Yucca", "Asparagaceae", ["Joshua tree"], 0.12, null)], null);

        var hit = PlantIdentificationExcerpt.ToHit(identification, minConfidence: 0.25, maxCandidates: 3);

        hit.Text.Should().StartWith(
            "Pl@ntNet is not sure what the plant in the screenshot is; its best guess is Yucca brevifolia (Joshua tree; family Asparagaceae) at only 12 % confidence, so say it is a guess.");
        hit.Text.Should().NotContain("Other candidates");
    }

    [Fact]
    public void ToHit_OffersNoMoreCandidatesThanAsked()
    {
        var identification = new PlantIdentification(
        [
            new PlantCandidate("A a", null, null, null, [], 0.5, null),
            new PlantCandidate("B b", null, null, null, [], 0.3, null),
            new PlantCandidate("C c", null, null, null, [], 0.1, null)
        ], null);

        PlantIdentificationExcerpt.ToHit(identification, 0.25, maxCandidates: 2).Text.Should().Contain("B b").And.NotContain("C c");
    }

    [Fact]
    public void TheSameBestMatch_IsTheSameExcerpt()
    {
        var one = new PlantIdentification([new PlantCandidate("Pinus kesiya", null, null, null, [], 0.6, null)], null);
        var two = new PlantIdentification([new PlantCandidate("Pinus kesiya", null, null, null, [], 0.6, null)], 10);

        PlantIdentificationExcerpt.ToHit(one, 0.25, 3).Id.Should().Be(PlantIdentificationExcerpt.ToHit(two, 0.25, 3).Id);
    }

    [Fact]
    public void NotRecognised_TellsTheModelNotToName_ALookAlike()
    {
        var hit = PlantIdentificationExcerpt.NotRecognised();

        hit.Text.Should().Contain("found no plant it recognises").And.Contain("a look-alike in a guide is only a look-alike");
        hit.Title.Should().Be("Pl@ntNet");
    }
}
