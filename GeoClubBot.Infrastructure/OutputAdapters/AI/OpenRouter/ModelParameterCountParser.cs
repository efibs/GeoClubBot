using System.Globalization;
using System.Text.RegularExpressions;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>
/// Reads a model's parameter counts from what the roster says about it, which has no field for them.
///
/// The id is read first, then the display name, then the provider's prose, and the total is taken from
/// the first of them that states one: an id like <c>nemotron-3-ultra-550b-a55b</c> is written by the
/// provider for exactly this, while prose also names other models ("near-31B quality"). Many ids carry
/// no size at all, though — of the free roster on 2026-10-23, Dots 3, Inkling and Nemotron 3.5 Super VL
/// stated theirs only in prose ("16B active parameters out of 280B total") — so the prose is read for the
/// phrases providers use to state their own size, and for nothing looser.
///
/// An active count is only ever read together with the total it is stated with, because prose compares
/// with mixtures of experts as readily as with dense models ("outperforms Qwen3-30B-A3B", "rivals MoE
/// models with 3B active parameters"). Taken on its own, such a count would turn a dense 14B model into a
/// 6.5B one and drop it below the selector's size bar. So the prose's active count must sit next to the
/// prose's own total, with no other count between them, and it completes a total from the id or the name
/// only when the prose states that same total — <c>gpt-oss-120b</c> calls itself "a 117B-parameter"
/// model that "activates 5.1B parameters". A size glued to a name by a hyphen (<c>Qwen3-30B-A3B</c>,
/// <c>Gemma-3-4B</c>) names a model rather than stating one, and a total the prose calls dense has no
/// active count at all.
///
/// A count nobody states stays unknown rather than guessed: the selector treats unknown as neutral, and
/// a misread size would rank a model wrongly where an unknown one merely ranks it in the middle.
/// </summary>
public static partial class ModelParameterCountParser
{
    public static ModelParameterCount Parse(string id, string? name, string? description)
    {
        ArgumentNullException.ThrowIfNull(id);

        // Each source states a total and, for a mixture of experts, the active count that goes with it.
        ModelParameterCount[] statements =
        [
            ParseToken(IdSizeToken(), Slug(id)),
            name is null ? default : ParseToken(NameSizeToken(), name),
            description is null ? default : ParseDescription(description)
        ];

        var total = statements.Select(statement => statement.TotalBillions).FirstOrDefault(count => count is not null);
        if (total is not { } chosenTotal)
        {
            return default;
        }

        // An active count belongs to the total it was stated with, so it only completes a total that
        // agrees with that one.
        var active = statements
            .Where(statement => statement is { TotalBillions: { } statedTotal, ActiveBillions: not null }
                                && Agree(statedTotal, chosenTotal))
            .Select(statement => statement.ActiveBillions)
            .FirstOrDefault();

        return new ModelParameterCount(chosenTotal, active);
    }

    /// <summary>
    /// The largest gap between two statements of the same model's total: ids round the true size, so
    /// <c>gpt-oss-120b</c> is 117B and <c>gemma-4-26b-a4b</c> 25.2B.
    /// </summary>
    private const double TotalTolerance = 0.1;

    private static bool Agree(double stated, double chosen) =>
        Math.Abs(stated - chosen) <= TotalTolerance * Math.Max(stated, chosen);

    /// <summary>
    /// The part of the id after the author, without the variant: <c>google/gemma-4-31b-it:free</c> reads
    /// as <c>gemma-4-31b-it</c>, so an author or variant that happens to end in a digit and "b" cannot pass
    /// for a size.
    /// </summary>
    private static string Slug(string id)
    {
        var slash = id.IndexOf('/', StringComparison.Ordinal);
        var slug = slash >= 0 ? id[(slash + 1)..] : id;
        var colon = slug.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 ? slug[..colon] : slug;
    }

    private static ModelParameterCount ParseToken(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        if (!match.Success)
        {
            return default;
        }

        var total = ToBillions(match.Groups["total"].Value, match.Groups["unit"].Value);
        var active = match.Groups["active"].Success ? ToBillions(match.Groups["active"].Value, "B") : null;
        return new ModelParameterCount(total, active);
    }

    private static ModelParameterCount ParseDescription(string description)
    {
        // The leftmost statement wins: a model's own size comes first, before any comparison with others.
        var totalMatch = DescriptionTotal().Match(description);
        if (!totalMatch.Success
            || ToBillions(totalMatch.Groups["n"].Value, totalMatch.Groups["u"].Value) is not { } total)
        {
            // An active count stated without a total cannot be told apart from a comparison.
            return default;
        }

        if (totalMatch.Groups["dense"].Success)
        {
            return new ModelParameterCount(total, null);
        }

        // The active count stated with this total: the nearest one with no other count between them.
        double? active = null;
        var nearest = int.MaxValue;
        foreach (Match activeMatch in DescriptionActive().Matches(description))
        {
            var (start, end) = activeMatch.Index < totalMatch.Index
                ? (activeMatch.Index + activeMatch.Length, totalMatch.Index)
                : (totalMatch.Index + totalMatch.Length, activeMatch.Index);
            if (end < start || DescriptionCount().IsMatch(description[start..end]))
            {
                continue;
            }

            if (ToBillions(activeMatch.Groups["n"].Value, activeMatch.Groups["u"].Value) is { } count
                && count < total
                && end - start < nearest)
            {
                active = count;
                nearest = end - start;
            }
        }

        return new ModelParameterCount(total, active);
    }

    private static double? ToBillions(string number, string unit)
    {
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        return unit.ToUpperInvariant() switch
        {
            "M" => value / 1000d,
            "T" or "TRILLION" => value * 1000d,
            _ => value
        };
    }

    /// <summary>
    /// A size token in an id: <c>31b</c>, <c>2.6b</c>, <c>350m</c>, or a mixture of experts' <c>550b-a55b</c>.
    /// Not preceded by a letter, digit or dot, so the <c>7b</c> of <c>8x7b</c> and the version in
    /// <c>lfm-2.5-2.6b</c> are not misread.
    /// </summary>
    [GeneratedRegex(
        @"(?<![a-z0-9.])(?<total>\d+(?:\.\d+)?)(?<unit>[bm])(?:-a(?<active>\d+(?:\.\d+)?)b)?(?![a-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdSizeToken();

    /// <summary>The same in a display name, where words are separated by spaces: <c>Xing4.0 29B A4B</c>.</summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9.])(?<total>\d+(?:\.\d+)?)(?<unit>[BM])(?:[\s-]+A(?<active>\d+(?:\.\d+)?)B)?(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameSizeToken();

    /// <summary>
    /// A count followed by what says it is the model's whole size: "40B total", "a 120B-parameter model",
    /// "a 30-billion-parameter model", "30.7B dense", "the 33B-A3B category" — not "12B parameters active",
    /// nor the "3B parameters" of "activating just 3B parameters". <c>dense</c> captures a total the prose
    /// calls dense. Not preceded by a hyphen, which glues the count to a model's name: <c>Qwen3-30B-A3B</c>.
    /// </summary>
    [GeneratedRegex(
        @"(?<!\bactivat\w*\s+(?:(?:just|only|about|roughly|around)\s+)?)"
        + @"(?<![\w.-])(?<n>\d+(?:\.\d+)?)" + Unit
        + @"(?=\s+total|[\s-]param(?>\w*)(?!\s+activ)(?<dense>\s+dense)?|(?<dense>\s+dense)|-A\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionTotal();

    /// <summary>
    /// A count said to be active per token: "12B active", "only 3.8B activate", "12B parameters active",
    /// "activating just 12B", or the second half of "33B-A3B" — but not of a name like <c>Qwen3-30B-A3B</c>.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\w.-])(?<n>\d+(?:\.\d+)?)" + Unit + @"(?=\s+activ|\s+param\w*\s+activ)"
        + @"|(?<=(?<![\w.-])\d+(?:\.\d+)?\s?B)-A(?<n>\d+(?:\.\d+)?)(?<u>B)(?![A-Za-z0-9])"
        + @"|\bactivat\w*\s+(?:(?:just|only|about|roughly|around)\s+)?(?<n>\d+(?:\.\d+)?)" + Unit + @"(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionActive();

    /// <summary>
    /// Any parameter count, a model's name included: one lying between a total and an active count
    /// means the two are not stated together.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9.])\d+(?:\.\d+)?" + Unit + @"(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionCount();

    /// <summary>
    /// The unit after a count in prose, captured as <c>u</c>: "12B", "12 B", "12 billion", and the
    /// hyphenated "12-billion" of "a 12-billion-parameter model". A hyphen is not allowed before a bare
    /// letter, where it would read <c>Qwen3-30B</c>'s name as a count.
    /// </summary>
    private const string Unit = @"(?:\s?(?<u>B|T)|[\s-]?(?<u>billion|trillion))";
}

/// <param name="TotalBillions">Every parameter of the model, or null when nothing states it.</param>
/// <param name="ActiveBillions">
/// Parameters active per token, for a mixture of experts; null for a dense model or when nothing states it.
/// </param>
public readonly record struct ModelParameterCount(double? TotalBillions, double? ActiveBillions);
