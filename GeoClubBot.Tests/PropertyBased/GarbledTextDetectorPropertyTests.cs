using CsCheck;
using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.PropertyBased;

/// <summary>
/// A reply is thrown away as garbled on one sign alone: words whose letters change alphabet half-way.
/// So text whose every word keeps to one alphabet must never be thrown away, whatever alphabets it
/// quotes and whatever stands between the words, and the verdict must depend only on how many words
/// are mixed, not on where they stand.
/// </summary>
public sealed class GarbledTextDetectorPropertyTests
{
    private static readonly string[] Alphabets =
    [
        "abcdefghijklmnopqrstuvwxyzABCéöłığşñ", "абвгдежзийклмнопрстуф", "αβγδεζηθικλμνξοπρσ", "कखगघचछजझटठडढ",
        "ابتثجحخدذرز", "אבגדהוזחטי", "აბგდევზთიკლ", "աբգդեզէըթժ"
    ];

    private static Gen<string> GenWordOf(int alphabet) =>
        Gen.Char[Alphabets[alphabet]].Array[1, 12].Select(letters => new string(letters));

    /// <summary>A word of one alphabet.</summary>
    private static readonly Gen<string> GenWord =
        Gen.Int[0, Alphabets.Length - 1].SelectMany(GenWordOf);

    /// <summary>A word that starts in one alphabet and ends in another.</summary>
    private static readonly Gen<string> GenMixedWord =
        Gen.Select(Gen.Int[0, Alphabets.Length - 1], Gen.Int[1, Alphabets.Length - 1])
            .SelectMany(pick => Gen.Select(
                GenWordOf(pick.Item1), GenWordOf((pick.Item1 + pick.Item2) % Alphabets.Length), (start, end) => start + end));

    /// <summary>What stands between words in an answer: spaces, punctuation, markup, digits, citations, a drawn line.</summary>
    private static readonly Gen<string> GenBetween =
        Gen.OneOf(
            Gen.Const(" "), Gen.Const(", "), Gen.Const(".\n\n"), Gen.Const(" [1] "), Gen.Const("/"), Gen.Const("-"),
            Gen.Const("0"), Gen.Const(" ── "), Gen.Const(" ("), Gen.Const("** "), Gen.Const("’"), Gen.Const(" 👍 "));

    [Fact]
    public void Text_whose_words_each_keep_to_one_alphabet_is_never_garbled() =>
        Gen.Select(GenWord, GenBetween).List[0, 120]
            .Sample(parts =>
            {
                var text = string.Concat(parts.Select(part => part.Item1 + part.Item2));

                GarbledTextDetector.IsGarbled(text).Should().BeFalse(text);
            });

    [Fact]
    public void The_verdict_counts_the_mixed_words_wherever_they_stand() =>
        Gen.Select(GenWord.List[0, 80], GenMixedWord.List[0, 6], Gen.Int[0, 10_000].List[6, 6])
            .Sample(input =>
            {
                var (clean, mixed, places) = input;

                var words = new List<string>(clean);
                for (var index = 0; index < mixed.Count; index++)
                {
                    words.Insert(places[index] % (words.Count + 1), mixed[index]);
                }

                var text = string.Join(' ', words);
                var expected = mixed.Count >= 2 && mixed.Count * 25 >= words.Count;

                GarbledTextDetector.IsGarbled(text).Should().Be(expected, text);
            });
}
