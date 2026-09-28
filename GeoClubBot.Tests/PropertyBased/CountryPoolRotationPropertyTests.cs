using CsCheck;
using FluentAssertions;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// The pool rotation promises "every country once before any comes round again", with nothing stored
/// but the history. These drive it pick after pick, as the weekly job would, over random pools and
/// seeds, and check the promise holds for every round.
/// </summary>
public sealed class CountryPoolRotationPropertyTests
{
    private static readonly Gen<IReadOnlyList<CountryPlan>> GenPool =
        Gen.Int[1, 8].Select(size => (IReadOnlyList<CountryPlan>)Enumerable.Range(0, size)
            .Select(i => new CountryPlan($"Country {i}", null, $"map-{i}", null, new GameSettings(0, false, false, false)))
            .ToList());

    /// <summary>A pool, a seed, and how many rounds to play it.</summary>
    private static readonly Gen<(IReadOnlyList<CountryPlan> Pool, int Seed, int Rounds)> GenRun =
        Gen.Select(GenPool, Gen.Int, Gen.Int[1, 5]);

    [Fact]
    public void Every_round_plays_each_country_exactly_once() =>
        GenRun.Sample(run =>
        {
            var history = Play(run.Pool, run.Seed, run.Pool.Count * run.Rounds);

            foreach (var round in history.Chunk(run.Pool.Count))
            {
                round.Should().BeEquivalentTo(run.Pool.Select(c => c.Name), "each round is a permutation of the pool");
            }
        });

    [Fact]
    public void Never_repeats_a_country_twice_in_a_row_when_there_is_a_choice() =>
        GenRun.Where(run => run.Pool.Count > 1).Sample(run =>
        {
            var history = Play(run.Pool, run.Seed, run.Pool.Count * run.Rounds + 1);

            history.Zip(history.Skip(1)).Should().OnlyContain(pair => pair.First != pair.Second);
        });

    [Fact]
    public void Always_picks_a_country_of_the_pool_even_with_foreign_history() =>
        Gen.Select(GenPool, Gen.String[Gen.Char.AlphaNumeric, 1, 12].List[0, 20], Gen.Int).Sample((pool, history, seed) =>
        {
            var pick = CountryPoolRotation.Pick(pool, history, new Random(seed));

            pool.Should().Contain(pick);
        });

    /// <summary>A country added part-way through a round is simply still unplayed in it.</summary>
    [Fact]
    public void A_country_added_mid_round_is_played_before_the_round_ends() =>
        GenRun.Where(run => run.Pool.Count > 2).Sample(run =>
        {
            var original = run.Pool.Take(run.Pool.Count - 1).ToList();
            var history = Play(original, run.Seed, 1);

            var rest = Play(run.Pool, run.Seed, run.Pool.Count - 1, history);

            rest.Should().Contain(run.Pool[^1].Name);
        });

    private static List<string> Play(IReadOnlyList<CountryPlan> pool, int seed, int picks, List<string>? history = null)
    {
        var random = new Random(seed);
        var played = history is null ? [] : new List<string>(history);
        var start = played.Count;

        for (var i = 0; i < picks; i++)
        {
            played.Add(CountryPoolRotation.Pick(pool, played, random).Name);
        }

        return played.Skip(start).ToList();
    }
}
