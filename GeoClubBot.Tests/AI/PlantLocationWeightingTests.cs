using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Pl@ntNet's candidates weighed by what GBIF records where the screenshot was taken, on the numbers
/// measured for the Bulgarian and Boston screenshots of #485 (2026-11-15).
/// </summary>
public sealed class PlantLocationWeightingTests
{
    private static readonly ScreenshotPlace Bulgaria = new("Bulgaria", "BG", PlacePrecision.Country, null, null, null, "in Bulgaria");
    private static readonly ScreenshotPlace Boston = new("Boston, Massachusetts, United States", "US", PlacePrecision.Point, 42.36, -71.06, null, "south of Boston");

    [Fact]
    public void PutsWhatGrowsThereFirst_AndWhatIsNeverRecordedThereLast()
    {
        // The picture's favourite is North American; Bulgaria has never recorded it.
        var identification = Identification(
            ("Pinus resinosa", 0.126, 1), ("Pinus strobus", 0.072, 2), ("Pinus nigra", 0.038, 3), ("Pinus sylvestris", 0.026, 4));
        var records = Records((1, 0), (2, 4), (3, 33), (4, 71));

        var weighing = PlantLocationWeighting.Weigh(identification, Bulgaria, records, records, recordsWeight: 0.5, unrecordedFactor: 0.1);

        weighing.IsWeighed.Should().BeTrue();
        var names = weighing.Candidates.Select(candidate => candidate.Candidate.ScientificName).ToList();
        names.Take(2).Should().BeEquivalentTo(["Pinus sylvestris", "Pinus nigra"], "both native, all but tied");
        names.Skip(2).Should().Equal("Pinus strobus", "Pinus resinosa");
        weighing.Candidates[^1].IsUnrecordedInCountry.Should().BeTrue();
        weighing.Candidates.Sum(candidate => candidate.Share).Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void KeepsAClearPicture_First_OverTreesRecordedMoreOftenThere()
    {
        var identification = Identification(("Picea abies", 0.523, 1), ("Picea sitchensis", 0.066, 2), ("Pinus strobus", 0.01, 3));
        var local = Records((1, 385), (2, 0), (3, 7_267));
        var country = Records((1, 7_019), (2, 8_766), (3, 81_318));

        var weighing = PlantLocationWeighting.Weigh(identification, Boston, local, country, 0.5, 0.1);

        weighing.Best.Candidate.ScientificName.Should().Be("Picea abies");
        weighing.IsIdentified(minConfidence: 0.25, minImageScore: 0.05).Should().BeTrue();
    }

    [Fact]
    public void NamesNoPlant_WhenOnlyTheRecordsLiftedIt()
    {
        // A hazy frame: every candidate about 1 %; white pine leads by its 7,267 records alone.
        var identification = Identification(("Ulmus americana", 0.013, 1), ("Acer rubrum", 0.013, 2), ("Pinus strobus", 0.010, 3));
        var records = Records((1, 209), (2, 2_182), (3, 7_267));

        var weighing = PlantLocationWeighting.Weigh(identification, Boston, records, records, 0.5, 0.1);

        weighing.Best.Candidate.ScientificName.Should().Be("Pinus strobus");
        weighing.Best.Share.Should().BeGreaterThan(0.25);
        weighing.IsIdentified(0.25, 0.05).Should().BeFalse("the picture gives white pine 1 %");
    }

    [Fact]
    public void KeepsPlNetsOrder_WithoutAPlace_OrWithoutItsRecords()
    {
        var identification = Identification(("Pinus resinosa", 0.126, 1), ("Pinus nigra", 0.038, 3));

        var unplaced = PlantLocationWeighting.Weigh(identification, null, null, null, 0.5, 0.1);
        var unread = PlantLocationWeighting.Weigh(identification, Bulgaria, null, null, 0.5, 0.1);

        foreach (var weighing in new[] { unplaced, unread })
        {
            weighing.IsWeighed.Should().BeFalse();
            weighing.Best.Candidate.ScientificName.Should().Be("Pinus resinosa");
            weighing.Best.Share.Should().Be(0.126, "without records the share is the image score");
            weighing.IsIdentified(0.25, 0.05).Should().BeFalse();
            weighing.IsIdentified(0.1, 0.05).Should().BeTrue();
        }

        unread.Place.Should().Be(Bulgaria);
    }

    [Fact]
    public void WeighsACandidateWithoutAGbifKey_AsRecordedNowhereNear_ButNotAsAbsent()
    {
        var identification = new PlantIdentification(
            [Candidate("Pinus unknown", 0.2, null), Candidate("Pinus nigra", 0.05, 3)], null);

        var weighing = PlantLocationWeighting.Weigh(identification, Bulgaria, Records((3, 33)), Records((3, 33)), 0.5, 0.1);

        var unknown = weighing.Candidates.Single(candidate => candidate.Candidate.ScientificName == "Pinus unknown");
        unknown.LocalRecords.Should().BeNull();
        unknown.IsUnrecordedInCountry.Should().BeFalse();
        weighing.Best.Candidate.ScientificName.Should().Be("Pinus nigra", "0.05 × √34 is more than 0.2 × 1");
    }

    [Fact]
    public void LeavesTheAbsencePenaltyOut_WhenTheCountryWasNotCounted()
    {
        var identification = Identification(("Pinus resinosa", 0.126, 1), ("Pinus nigra", 0.038, 3));

        var weighing = PlantLocationWeighting.Weigh(identification, Boston, Records((3, 33)), countryRecords: null, 0.5, 0.1);

        weighing.Candidates.Should().OnlyContain(candidate => !candidate.IsUnrecordedInCountry);
        weighing.Candidates.Single(candidate => candidate.Candidate.ScientificName == "Pinus resinosa").LocalRecords.Should().Be(0);
    }

    private static PlantIdentification Identification(params (string Name, double Score, long Key)[] candidates) =>
        new([.. candidates.Select(candidate => Candidate(candidate.Name, candidate.Score, candidate.Key))], 480);

    private static PlantCandidate Candidate(string name, double score, long? key) =>
        new(name, null, name.Split(' ')[0], "Pinaceae", [], score, key);

    private static IReadOnlyDictionary<long, long> Records(params (long Key, long Count)[] records) =>
        records.Where(record => record.Count > 0).ToDictionary(record => record.Key, record => record.Count);
}
