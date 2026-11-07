using System.Text.RegularExpressions;
using CsCheck;
using FluentAssertions;
using UseCases.UseCases.ClubChatForwarding;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// GeoGuessr silently drops whatever goes past its chat limit, so the splitter must never hand it a
/// longer part, must lose nothing, and must number the parts 1..n, whatever the text.
/// </summary>
public sealed partial class ClubChatTextSplitterPropertyTests
{
    /// <summary>Words, spaces, line and paragraph breaks, sentence ends and the odd emoji, in random runs.</summary>
    private static readonly Gen<string> GenText =
        Gen.OneOf(
                Gen.String[Gen.Char.AlphaNumeric, 1, 30],
                Gen.Const(" "), Gen.Const(". "), Gen.Const("\n"), Gen.Const("\n\n"), Gen.Const("😀"),
                Gen.String[Gen.Char.AlphaNumeric, 200, 400])
            .List[0, 80]
            .Select(string.Concat);

    private static readonly Gen<(string Text, int MaxLength, int MaxParts)> GenInput =
        Gen.Select(GenText, Gen.Int[20, 400], Gen.Int[1, 20]);

    [Fact]
    public void No_part_is_longer_than_the_limit() =>
        GenInput.Sample(input =>
        {
            var result = ClubChatTextSplitter.Split(input.Text, input.MaxLength, input.MaxParts);

            if (result.IsSuccess)
            {
                result.Value.Should().OnlyContain(p => p.Length <= input.MaxLength && p.Trim().Length > 0);
            }
        });

    [Fact]
    public void Nothing_but_whitespace_at_the_cuts_is_lost() =>
        GenInput.Sample(input =>
        {
            var result = ClubChatTextSplitter.Split(input.Text, input.MaxLength, int.MaxValue / 2);

            if (result.IsSuccess)
            {
                var rejoined = string.Concat(result.Value.Select(p => Numbering().Replace(p, string.Empty, 1)));
                Whitespace().Replace(rejoined, string.Empty).Should().Be(Whitespace().Replace(input.Text, string.Empty));
            }
        });

    [Fact]
    public void Parts_are_numbered_one_to_n_or_not_at_all() =>
        GenInput.Sample(input =>
        {
            var result = ClubChatTextSplitter.Split(input.Text, input.MaxLength, input.MaxParts);

            if (result.IsSuccess && result.Value.Count > 1)
            {
                var count = result.Value.Count;
                result.Value.Select((p, i) => p.StartsWith($"({i + 1}/{count}) ", StringComparison.Ordinal)).Should().OnlyContain(ok => ok);
            }
            else if (result.IsSuccess)
            {
                result.Value.Single().Should().Be(input.Text.Trim());
            }
        });

    [Fact]
    public void Is_refused_only_for_an_empty_text_or_too_many_parts() =>
        GenInput.Sample(input =>
        {
            var result = ClubChatTextSplitter.Split(input.Text, input.MaxLength, input.MaxParts);
            var unlimited = ClubChatTextSplitter.Split(input.Text, input.MaxLength, int.MaxValue / 2);

            if (string.IsNullOrWhiteSpace(input.Text))
            {
                result.Error.Code.Should().Be("club-chat.empty");
            }
            else if (unlimited.Value.Count > input.MaxParts)
            {
                result.Error.Code.Should().Be("club-chat.too_long");
            }
            else
            {
                result.IsSuccess.Should().BeTrue();
            }
        });

    [GeneratedRegex(@"^\(\d+/\d+\) ")]
    private static partial Regex Numbering();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
