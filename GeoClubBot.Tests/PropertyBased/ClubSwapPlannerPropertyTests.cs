using CsCheck;
using Entities;
using FluentAssertions;
using UseCases.UseCases.ClubMemberActivity.ClubSwaps;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of the swap suggestions over arbitrary clubs: nobody moves twice, every swap beats
/// the buffer, and the main club never ends up over its size limit.
/// </summary>
public sealed class ClubSwapPlannerPropertyTests
{
    private static readonly Gen<List<ClubMemberAverageXp>> GenMain =
        Gen.Double[0, 300].List[0, 12].Select(averages => averages
            .Select((avg, i) => new ClubMemberAverageXp($"M{i}", avg, DateTimeOffset.UnixEpoch.AddDays(i), $"main-{i}"))
            .ToList());

    private static readonly Gen<List<SwapCandidate>> GenDen =
        Gen.Double[0, 300].List[0, 12].Select(averages => averages
            .Select((avg, i) => new SwapCandidate($"den-{i}", $"D{i}", avg, "Den"))
            .ToList());

    private static readonly Gen<SwapPlanOptions> GenOptions =
        Gen.Select(Gen.Int[0, 30], Gen.Int[1, 15], Gen.Bool, Gen.Bool,
            (buffer, size, replace, fill) => new SwapPlanOptions(buffer, size, replace, fill));

    [Fact]
    public void Nobody_is_used_twice_and_every_swap_beats_the_buffer() =>
        Gen.Select(GenMain, GenDen, GenOptions, Gen.Int[0, 3]).Sample((main, den, options, kickedCount) =>
        {
            var kicked = main.Take(Math.Min(kickedCount, main.Count)).ToList();

            var suggestions = ClubSwapPlanner.Plan(main, den, kicked, main.Count, options);

            suggestions.Select(s => s.Incoming.UserId).Should().OnlyHaveUniqueItems();
            suggestions.Where(s => s.Outgoing is not null).Select(s => s.Outgoing!.UserId).Should().OnlyHaveUniqueItems();

            suggestions.Where(s => s.Reason == SwapReason.BetterAverage)
                .Should().OnlyContain(s => s.Incoming.AverageXp >= s.Outgoing!.AverageXp + options.BufferXp);

            var promotions = suggestions.Count(s => s.Reason == SwapReason.FillsFreeSpot);
            (main.Count + promotions).Should().BeLessThanOrEqualTo(Math.Max(main.Count, options.MaxClubSize));
        });

    [Fact]
    public void Better_candidates_are_never_passed_over_for_worse_ones() =>
        Gen.Select(GenMain, GenDen, GenOptions).Sample((main, den, options) =>
        {
            var suggestions = ClubSwapPlanner.Plan(main, den, [], main.Count, options);
            var chosen = suggestions.Select(s => s.Incoming.UserId).ToHashSet();

            var worstChosen = suggestions.Select(s => s.Incoming.AverageXp).DefaultIfEmpty(double.MaxValue).Min();
            den.Where(c => !chosen.Contains(c.UserId))
                .Should().OnlyContain(c => c.AverageXp <= worstChosen);
        });
}
