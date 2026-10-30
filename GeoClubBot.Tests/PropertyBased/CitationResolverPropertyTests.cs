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
/// Answers are generated as prose mixed with every citation style free models were seen writing — a
/// range written as two citations joined by a dash and the prompt's own "(picture)" notes copied after
/// one among them — and with numbers beyond what was offered, since models invent those too. Each line
/// starts with a word, as real answers do; a line of nothing but citations is removed on purpose and
/// covered by its own example test.
/// </summary>
public sealed class CitationResolverPropertyTests
{
    private const int MaxImages = 3;

    private static readonly string[] Words =
    [
        "Bollards", "in", "Ghana", "are", "white.", "The", "poles", "have", "**red**", "stripes,",
        "and", "(see", "above)", "plates", "look", "yellow;", "-", "Khasi", "pines", "grow", "here."
    ];

    /// <summary>One citation token in one of the styles models were seen writing.</summary>
    private static readonly Gen<string> GenCitation =
        Gen.Select(Gen.Int[0, 13], Gen.Int[1, 12], Gen.Int[1, 12], (style, first, second) => style switch
        {
            11 => $"[{first}]–[{second}]",
            12 => $"[{first}] (picture, usually with a distribution map)",
            13 => $"([{first}] - [{second}])",
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

    /// <summary>
    /// Up to eight excerpts, drawing on few enough pages, sections and pictures that duplicates occur —
    /// two sections of one page, as a Google Doc's chunks are, and a picture that comes with one excerpt
    /// and among another's further pictures, as merged slide copies do.
    /// </summary>
    private static readonly Gen<IReadOnlyList<OfferedExcerpt>> GenOffered =
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1], Gen.Int[0, 5], Gen.Bool, Gen.Int[0, 5].List[0, 3],
                (page, section, picture, isPicture, extras) => (page, section, picture, isPicture, extras))
            .List[0, 8]
            .Select(IReadOnlyList<OfferedExcerpt> (entries) =>
            [
                .. entries.Select((entry, index) => new OfferedExcerpt(
                    index + 1,
                    $"https://guide/{entry.page}",
                    $"Guide {entry.page} > Section {entry.section}",
                    entry.isPicture ? $"https://img/{entry.picture}.png" : null,
                    entry.isPicture ? $"Picture {entry.picture}" : null)
                {
                    ExtraImageUrls = entry.isPicture
                        ? [.. entry.extras.Where(extra => extra != entry.picture).Distinct().Select(extra => $"https://img/{extra}.png")]
                        : []
                })
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

            text.Should().NotContain("【").And.NotContain("[image").And.NotContain("†").And.NotContain("****")
                .And.NotContain("(picture").And.NotContain("distribution map");
            Regex.Matches(text, @"\[[^\[\]]*\]")
                .Should().OnlyContain(match => Regex.IsMatch(match.Value, @"^\[[1-9][0-9]*\]$"));
        });

    [Fact]
    public void Attached_pictures_are_cited_pictures_within_the_cap() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);
            var pictured = result.Sources.Where(source => source.IsImage).ToList();

            // Each comes with an excerpt the answer cited, under that excerpt's number.
            result.Images.Should().OnlyContain(image =>
                pictured.Any(source => source.Number == image.Number && source.Excerpt == image.Excerpt)
                && image.Excerpt.ImageUrls.Contains(image.ImageUrl));

            // None twice, and as many as the cap allows of all the cited excerpts bring.
            result.Images.Select(image => image.ImageUrl).Should().OnlyHaveUniqueItems();
            result.Images.Should().HaveCount(Math.Min(
                MaxImages, pictured.SelectMany(source => source.Excerpt.ImageUrls).Distinct().Count()));
        });

    [Fact]
    public void Every_cited_picture_shows_its_first_picture_before_any_shows_a_second() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            var firsts = result.Sources
                .Where(source => source.IsImage)
                .Select(source => source.Excerpt.ImageUrls[0])
                .Distinct()
                .Take(MaxImages)
                .ToList();

            if (firsts.Count > 0)
            {
                result.Images.Select(image => image.ImageUrl).Should().Contain(firsts);
            }
        });

    [Fact]
    public void Each_excerpts_pictures_are_counted_in_number_order() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            result.Images.Select(image => image.Number).Should().BeInAscendingOrder();
            foreach (var group in result.Images.GroupBy(image => image.Number))
            {
                group.Select(image => image.Position).Should().Equal(Enumerable.Range(1, group.Count()));
                group.Should().OnlyContain(image => image.Count == group.Count());
            }
        });

    [Fact]
    public void The_cited_excerpts_are_the_ones_behind_the_sources_in_number_order() =>
        Gen.Select(GenAnswer, GenOffered).Sample((answer, offered) =>
        {
            var result = CitationResolver.Resolve(answer, offered, MaxImages);

            result.CitedExcerpts.Should().OnlyHaveUniqueItems().And.OnlyContain(excerpt => offered.Contains(excerpt));
            foreach (var source in result.Sources)
            {
                result.CitedExcerpts.Should().Contain(source.Excerpt, "every listed source is a cited excerpt");
            }

            result.CitedExcerpts
                .Select(excerpt => result.Sources.Single(source => KeyOf(source.Excerpt) == KeyOf(excerpt)).Number)
                .Should().BeInAscendingOrder();
        });

    [Fact]
    public void Stripping_removes_every_citation_and_is_idempotent() =>
        GenAnswer.Sample(answer =>
        {
            var stripped = CitationResolver.StripMarkers(answer);

            NumbersIn(stripped).Should().BeEmpty();
            CitationResolver.StripMarkers(stripped).Should().Be(stripped);
        });

    /// <summary>What makes two excerpts one source: a picture by its picture, text by its page and section.</summary>
    private static string KeyOf(OfferedExcerpt excerpt) => excerpt.ImageUrl ?? $"{excerpt.SourceUrl}|{excerpt.Label}";

    private static IEnumerable<int> NumbersIn(string text) =>
        Regex.Matches(text, @"\[([0-9]+)\]").Select(match => int.Parse(match.Groups[1].Value));
}
