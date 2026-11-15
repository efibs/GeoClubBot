using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What the model is told about the screenshot's plant, so it names it from the service rather than a
/// look-alike guide picture — and, when the member said where it was taken, what grows there.
/// </summary>
public sealed class PlantIdentificationExcerptTests
{
    private static readonly ScreenshotPlace Boston = new("Boston, Massachusetts, United States", "US", PlacePrecision.Point, 42.36, -71.06, null, "south of Boston");
    private static readonly ScreenshotPlace Bulgaria = new("Bulgaria", "BG", PlacePrecision.Country, null, null, null, "in Bulgaria");

    [Fact]
    public void ToHit_StatesTheIdentification_WithItsConfidence_AndTheRunnersUp()
    {
        var identification = new PlantIdentification(
        [
            new PlantCandidate("Pinus kesiya", "Royle ex Gordon", "Pinus", "Pinaceae", ["Khasi pine", "Benguet pine"], 0.612, 5285824),
            new PlantCandidate("Pinus roxburghii", "Sarg.", "Pinus", "Pinaceae", ["Chir pine"], 0.21, 5285750),
            new PlantCandidate("Pinus merkusii", null, "Pinus", "Pinaceae", [], 0.05, null)
        ], RemainingRequests: 480);

        var hit = ToHit(identification, place: null);

        hit.Kind.Should().Be(KnowledgeChunkKind.Text);
        hit.Title.Should().Be("Pl@ntNet");
        hit.SectionPath.Should().Be("What plant the screenshot shows (Pl@ntNet)");
        hit.SourceUrl.Should().Be("https://identify.plantnet.org/");
        hit.Text.Should().Be(
            "Pl@ntNet, an image-based plant identification service, identifies the plant in the screenshot as Pinus kesiya (Khasi pine, Benguet pine; family Pinaceae), 61 % from the picture. "
            + "Other candidates: Pinus roxburghii (Chir pine; family Pinaceae) (21 %); Pinus merkusii (family Pinaceae) (5 %). "
            + "The identification is by image recognition and can be wrong; name the plant with its scientific name and the confidence.");
    }

    [Fact]
    public void ToHit_CallsAWeakBestMatchAGuess()
    {
        var identification = new PlantIdentification(
            [new PlantCandidate("Yucca brevifolia", null, "Yucca", "Asparagaceae", ["Joshua tree"], 0.12, null)], null);

        var hit = ToHit(identification, place: null);

        hit.Text.Should().StartWith(
            "Pl@ntNet, an image-based plant identification service, is not sure what the plant in the screenshot is; its best guess is Yucca brevifolia (Joshua tree; family Asparagaceae) at only 12 % confidence, so say it is a guess.");
        hit.Text.Should().NotContain("Other candidates");
    }

    [Fact]
    public void ToHit_GivesTheGenus_WhenPlNetIsSureOfIt()
    {
        var identification = new PlantIdentification([new PlantCandidate("Pinus resinosa", null, "Pinus", "Pinaceae", ["Red pine"], 0.126, 1)], null)
        {
            Genera = [new PlantGenusCandidate("Pinus", "Pinaceae", ["Pine", "Pines"], 0.829), new PlantGenusCandidate("Larix", "Pinaceae", [], 0.03)]
        };

        ToHit(identification, place: null).Text.Should().Contain(" Its genus is clearer: Pinus (Pine, Pines; family Pinaceae), 83 %.");
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

        ToHit(identification, place: null, maxCandidates: 2).Text.Should().Contain("B b").And.NotContain("C c");
    }

    [Fact]
    public void ToHit_SaysTinyScoresAreUnderOnePercent_NeverZero()
    {
        var identification = new PlantIdentification(
            [new PlantCandidate("A a", null, null, null, [], 0.3, null), new PlantCandidate("B b", null, null, null, [], 0.004, null)], null);

        ToHit(identification, place: null).Text.Should().Contain("B b (under 1 %)").And.NotContain("(0 %)");
    }

    [Fact]
    public void TheSameBestMatch_IsTheSameExcerpt_ForTheSamePlace()
    {
        var one = new PlantIdentification([new PlantCandidate("Pinus kesiya", null, null, null, [], 0.6, null)], null);
        var two = new PlantIdentification([new PlantCandidate("Pinus kesiya", null, null, null, [], 0.6, null)], 10);

        ToHit(one, place: null).Id.Should().Be(ToHit(two, place: null).Id);
        ToHit(one, Boston).Id.Should().NotBe(ToHit(one, place: null).Id);
    }

    [Fact]
    public Task ToHit_WeighedByWhereItWasTaken_NamesTheRecordsAndWhatIsUnlikelyThere()
    {
        // Measured on the screenshots of #485: an identified spruce near Boston, an uncertain pine in Bulgaria.
        var spruce = new PlantIdentification(
        [
            new PlantCandidate("Picea abies", null, "Picea", "Pinaceae", ["Norway Spruce", "Common Spruce"], 0.523, 1),
            new PlantCandidate("Picea sitchensis", null, "Picea", "Pinaceae", ["Sitka spruce"], 0.066, 2),
            new PlantCandidate("Larix decidua", null, "Larix", "Pinaceae", ["European larch"], 0.040, 3)
        ], null)
        {
            Genera = [new PlantGenusCandidate("Picea", "Pinaceae", ["Spruce"], 0.604)]
        };
        var spruceRecords = Records((1, 385), (3, 17));
        var spruceCountry = Records((1, 7_019), (2, 8_766), (3, 738));

        var pine = new PlantIdentification(
        [
            new PlantCandidate("Pinus resinosa", null, "Pinus", "Pinaceae", ["Red pine"], 0.126, 11),
            new PlantCandidate("Pinus strobus", null, "Pinus", "Pinaceae", ["Eastern white pine"], 0.072, 12),
            new PlantCandidate("Pinus nigra", null, "Pinus", "Pinaceae", ["Black pine"], 0.038, 13),
            new PlantCandidate("Pinus sylvestris", null, "Pinus", "Pinaceae", ["Scots pine"], 0.026, 14)
        ], null)
        {
            Genera = [new PlantGenusCandidate("Pinus", "Pinaceae", [], 0.829)]
        };
        var pineRecords = Records((12, 4), (13, 33), (14, 71));

        var unread = new PlantIdentification([new PlantCandidate("Picea abies", null, "Picea", "Pinaceae", [], 0.523, 1)], null);

        return Verify(string.Join("\n\n",
            ToHit(spruce, PlantLocationWeighting.Weigh(spruce, Boston, spruceRecords, spruceCountry, 0.5, 0.1)).Text,
            ToHit(pine, PlantLocationWeighting.Weigh(pine, Bulgaria, pineRecords, pineRecords, 0.5, 0.1)).Text,
            ToHit(unread, PlantLocationWeighting.Weigh(unread, Boston, null, null, 0.5, 0.1)).Text));
    }

    [Fact]
    public void NotRecognised_TellsTheModelNotToName_ALookAlike()
    {
        var hit = PlantIdentificationExcerpt.NotRecognised();

        hit.Text.Should().Contain("found no plant it recognises").And.Contain("a look-alike in a guide is only a look-alike");
        hit.Title.Should().Be("Pl@ntNet");
    }

    private static KnowledgeHit ToHit(PlantIdentification identification, ScreenshotPlace? place, int maxCandidates = 3) =>
        ToHit(identification, PlantLocationWeighting.Weigh(identification, place, null, null, 0.5, 0.1), maxCandidates);

    private static KnowledgeHit ToHit(PlantIdentification identification, PlantWeighing weighing, int maxCandidates = 3) =>
        PlantIdentificationExcerpt.ToHit(identification, weighing, minConfidence: 0.25, minImageScore: 0.05, maxCandidates, radiusKm: 50);

    private static IReadOnlyDictionary<long, long> Records(params (long Key, long Count)[] records) =>
        records.ToDictionary(record => record.Key, record => record.Count);
}
