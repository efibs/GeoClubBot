using CsCheck;
using FluentAssertions;
using GeoClubBot.Tests.AI;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of weighing a screenshot's plant candidates by where it was taken, over arbitrary scores
/// and record counts — and of reading the place, over arbitrary text: the weighing loses and invents no
/// candidate, its shares are a distribution in order, more records never push a candidate down, and
/// without the records' weight Pl@ntNet's order stands; the reader never throws and never places a
/// screenshot off the globe.
/// </summary>
public sealed class PlantLocationWeightingPropertyTests
{
    /// <summary>Pieces of what members write about places, and of coordinates, to be stuck together at random.</summary>
    private static readonly string[] Words =
    [
        "in", "near", "south of", "from", "it's", "", "@", "q=", ",", "°", "N", "W", "Boston", "Montana", "MA",
        "Bulgaria", "norway spruce", "42.2511", "-71.0023", "123.456789", "99.999", "what tree is this?", "Georgia", "USA"
    ];

    private static readonly ScreenshotPlace Place = new("Bulgaria", "BG", PlacePrecision.Country, null, null, null, "in Bulgaria");

    /// <summary>Up to 30 candidates, keys 1..n, scores falling as Pl@ntNet's do, records from none to many.</summary>
    private static readonly Gen<(PlantIdentification Identification, Dictionary<long, long> Records)> GenCase =
        Gen.Select(Gen.Double[0.0001, 1].List[1, 30], Gen.Long[0, 100_000].List[30, 30], Gen.Bool.List[30, 30], (scores, counts, recorded) =>
        {
            var candidates = scores.OrderByDescending(score => score)
                .Select((score, index) => new PlantCandidate($"Species {index}", null, null, null, [], score, index + 1))
                .ToList();
            var records = candidates
                .Where(candidate => recorded[(int)candidate.GbifTaxonKey!.Value - 1])
                .ToDictionary(candidate => candidate.GbifTaxonKey!.Value, candidate => counts[(int)candidate.GbifTaxonKey!.Value - 1]);
            return (new PlantIdentification(candidates, null), records);
        });

    [Fact]
    public void Weighs_every_candidate_once_into_shares_in_order() =>
        Gen.Select(GenCase, Gen.Double[0, 2], Gen.Double[0, 1]).Sample((@case, weight, factor) =>
        {
            var weighing = PlantLocationWeighting.Weigh(@case.Identification, Place, @case.Records, @case.Records, weight, factor);

            weighing.Candidates.Select(candidate => candidate.Candidate).Should().BeEquivalentTo(@case.Identification.Candidates);
            weighing.Candidates.Should().OnlyContain(candidate => candidate.Share >= 0 && candidate.Share <= 1 + 1e-9);
            weighing.Candidates.Select(candidate => candidate.Share).Should().BeInDescendingOrder();
            if (weighing.Candidates.Sum(candidate => candidate.Share) is var total && total > 0)
            {
                total.Should().BeApproximately(1, 1e-6);
            }
        });

    [Fact]
    public void Never_ranks_a_candidate_below_one_it_matches_or_beats_in_both_picture_and_records() =>
        Gen.Select(GenCase, Gen.Double[0, 2], Gen.Double[0, 1]).Sample((@case, weight, factor) =>
        {
            var weighing = PlantLocationWeighting.Weigh(@case.Identification, Place, @case.Records, @case.Records, weight, factor);
            var rank = weighing.Candidates.Select((candidate, index) => (candidate, index)).ToDictionary(entry => entry.candidate.Candidate.GbifTaxonKey!.Value, entry => entry.index);

            foreach (var better in weighing.Candidates)
            {
                foreach (var worse in weighing.Candidates)
                {
                    if (better.Candidate.Score > worse.Candidate.Score && (better.LocalRecords ?? 0) > (worse.LocalRecords ?? 0))
                    {
                        rank[better.Candidate.GbifTaxonKey!.Value].Should().BeLessThan(rank[worse.Candidate.GbifTaxonKey!.Value]);
                    }
                }
            }
        });

    [Fact]
    public void Keeps_PlNets_order_when_the_records_weigh_nothing() =>
        GenCase.Sample(@case =>
        {
            var everywhere = @case.Identification.Candidates.ToDictionary(candidate => candidate.GbifTaxonKey!.Value, _ => 1L);

            var weighing = PlantLocationWeighting.Weigh(@case.Identification, Place, @case.Records, everywhere, recordsWeight: 0, unrecordedFactor: 0.1);

            weighing.Candidates.Select(candidate => candidate.Candidate.Score).Should().BeInDescendingOrder();
        });

    [Fact]
    public void Reading_a_place_never_throws_and_never_leaves_the_globe() =>
        Gen.Select(
                Gen.Int[0, Words.Length - 1].Select(index => Words[index]),
                Gen.Char.AlphaNumeric.Array[0, 3].Select(chars => new string(chars)))
            .List[0, 12]
            .Select(parts => string.Join(' ', parts.Select(part => part.Item1 + part.Item2)))
            .Sample(text =>
            {
                var place = ScreenshotPlaceReader.Find(text, PlaceFixtures.Index, IsoCountries.Gazetteer);

                if (place is { Latitude: { } latitude, Longitude: { } longitude })
                {
                    Math.Abs(latitude).Should().BeLessThanOrEqualTo(90);
                    Math.Abs(longitude).Should().BeLessThanOrEqualTo(180);
                }
            });
}
