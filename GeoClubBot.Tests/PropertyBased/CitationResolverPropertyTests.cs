using System.Text.RegularExpressions;
using CsCheck;
using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// Invariants of the citation resolver over arbitrary answers. These are the properties a reader
/// notices when they break: a number in the prose that leads nowhere, numbers that do not run 1, 2, 3,
/// or a citation style that reaches Discord as literal text.
///
/// Answers are generated as prose mixed with every citation style free models were seen writing, and
/// with numbers beyond what was offered, since models invent those too. Each line starts with a word,
/// as real answers do; a line of nothing but citations is removed on purpose and covered by its own
/// example test.
/// </summary>
public sealed class CitationResolverPropertyTests
{
    private const int MaxImages = 2;

    private static readonly string[] Words =
    [
        "Bollards", "in", "Ghana", "are", "white.", "The", "poles", "have", "**red**", "stripes,",
        "and", "(see", "above)", "plates", "look", "yellow;", "-", "Khasi", "pines", "grow", "here."
    ];

    /// <summary>One citation token in one of the styles models were seen writing.</summary>
    private static readonly Gen<string> GenCitation =
        Gen.Select(Gen.Int[0, 10], Gen.Int[1, 12], Gen.Int[1, 12], (style, first, second) => style switch
        {
            0 => $"[{first}]",
            1 => $"[image {first}]",
            2 => $"**[image {first}]**",
            3 => $"【{first}】",
            4 => $"【image {first}】",
            5 => $"[{first}, {second}]",
            6 => $"[{Math.Min(first, second)}-{Math.Max(first, second)}]",
            7 => $"([{first}], [{second}])",
            8 => $"[^{first}]",
            9 => $"【{first}:0†source】",
            _ => $"`[{first}]`"
        });

    private static readonly Gen<string> GenToken =
        Gen.Frequency(
            (6, Gen.Int[0, Words.Length - 1].Select(index => Words[index])),
            (3, GenCitation),
            (1, Gen.Const("\n")));

    /// <summary>Tokens joined by spaces; every line opens with a word so none is citations alone.</summary>
    private static readonly Gen<string> GenAnswer =
        GenToken.List[0, 30].Select(tokens =>
        {
            var answer = string.Join(" ", tokens).Replace("\n ", "\nWord ", StringComparison.Ordinal);
            return "Answer " + answer;
        });

    /// <summary>Up to eight excerpts, drawing on few enough pages and pictures that duplicates occur.</summary>
    private static readonly Gen<IReadOnlyList<OfferedExcerpt>> GenOffered =
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 3], Gen.Bool, (page, picture, isPicture) => (page, picture, isPicture))
            .List[0, 8]
            .Select(IReadOnlyList<OfferedExcerpt> (entries) =>
            [
                .. entries.Select((entry, index) => new OfferedExcerpt(
                    index + 1,
                    $"https://guide/{entry.page}",
                    $"Guide {entry.page}",
                    entry.isPicture ? $"https://img/{entry.picture}.png" : null,
                    entry.isPicture ? $"Picture {entry.picture}" : null))
            ]);

    [Fact]
    public void Numbers_run_from_one_in_order_of_first_mention() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            var firstMentions = NumbersIn(result.Text).Distinct().ToList();

            firstMentions.Should().Equal(Enumerable.Range(1, result.Sources.Count));
            result.Sources.Select(source => source.Number).Should().Equal(Enumerable.Range(1, result.Sources.Count));
        });

    [Fact]
    public void Every_number_in_the_answer_leads_somewhere() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            NumbersIn(result.Text).Should().OnlyContain(number => number >= 1 && number <= result.Sources.Count);
        });

    [Fact]
    public void No_other_citation_style_reaches_the_reader() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var text = CitationResolver.Resolve(answer, offered, MaxImages).Text;

            text.Should().NotContain("【").And.NotContain("[image").And.NotContain("†").And.NotContain("****");
            Regex.Matches(text, @"\[[^\[\]]*\]")
                .Should().OnlyContain(match => Regex.IsMatch(match.Value, @"^\[[1-9][0-9]*\]$"));
        });

    [Fact]
    public void Attached_pictures_are_cited_pictures_within_the_cap() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            result.Images.Should().OnlyContain(image => image.IsImage && result.Sources.Contains(image));
            result.Images.Should().HaveCountLessThanOrEqualTo(MaxImages);
            result.Images.Should().HaveCount(Math.Min(MaxImages, result.Sources.Count(source => source.IsImage)));
        });

    [Fact]
    public void Stripping_removes_every_citation_and_is_idempotent() =>
        GenAnswer.Sample(answer =>
        {
            var stripped = CitationResolver.StripMarkers(answer);

            NumbersIn(stripped).Should().BeEmpty();
            CitationResolver.StripMarkers(stripped).Should().Be(stripped);
        });

    private static IEnumerable<int> NumbersIn(string text) =>
        Regex.Matches(text, @"\[([0-9]+)\]").Select(match => int.Parse(match.Groups[1].Value));
}
