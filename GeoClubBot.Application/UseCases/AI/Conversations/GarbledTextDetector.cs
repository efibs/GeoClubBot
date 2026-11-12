using System.Globalization;

namespace UseCases.UseCases.AI.Conversations;

/// <summary>
/// Tells a reply that is not language at all from an answer.
///
/// A provider serving a broken copy of a model returns a perfectly valid completion made of tokens
/// from everywhere at once. This is nemotron-3-ultra, the model that answers most questions, three
/// seconds after being asked for a clue from outside India (2026-11-11):
/// <code>
/// .reurs(.तuotfrac────ζ Beliaufu  fsh──)t.frac──  …  ├── tomon0première WedługNuméroθ
/// </code>
/// Nothing failed, so it would have been posted with the closest guides credited beneath it. The same
/// model had answered the same question properly the day before, so the reply says nothing against
/// the model: it is discarded and other models are asked, but the model is not kept out.
///
/// The sign looked for is one no language produces and such a reply is full of: a word whose letters
/// change alphabet half-way — "तuotfrac", "WedługNuméroθ" — four of that reply's sixteen words. Real
/// answers do quote other alphabets (a Cyrillic place name, a Greek road sign), but as words of their
/// own. Scripts written without spaces between words are left out of it: "JR東日本" and "ซอยSukhumvit"
/// run Latin letters into theirs as a matter of course.
///
/// Deliberately narrow. A reply is discarded on that sign alone, never for being short, odd or wrong:
/// a detector that fired on a real answer would throw it away and spend a request replacing it.
///
/// Pure, so the rule is pinned by tests.
/// </summary>
public static class GarbledTextDetector
{
    /// <summary>
    /// Words of mixed alphabets a reply must hold to be discarded. One can be a slip: "kΩ", or a
    /// look-alike letter in a place name.
    /// </summary>
    private const int MinMixedWords = 2;

    /// <summary>
    /// …and they must be at least one word in this many. Two slips in a long answer are still slips;
    /// the recorded reply had one in four.
    /// </summary>
    private const int WordsPerMixedWord = 25;

    private const int NotALetter = -1;

    /// <summary>A letter that belongs to whatever word it stands in, and makes none mixed.</summary>
    private const int AnyAlphabet = 0;

    private const int Latin = 1;
    private const int Greek = 2;
    private const int Cyrillic = 3;
    private const int Armenian = 4;
    private const int Hebrew = 5;
    private const int Arabic = 6;
    private const int Georgian = 7;
    private const int Ethiopic = 8;
    private const int Cherokee = 9;
    private const int Syllabics = 10;
    private const int Mongolian = 11;

    /// <summary>Alphabets not listed by name: each block of 128 code points is taken as one, as the Indic scripts are laid out.</summary>
    private const int FirstUnnamedAlphabet = 100;

    /// <summary>Whether <paramref name="text"/> is token soup rather than language.</summary>
    public static bool IsGarbled(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var words = 0;
        var mixedWords = 0;

        var inWord = false;
        var alphabet = AnyAlphabet;
        var isMixed = false;

        foreach (var character in text)
        {
            var of = AlphabetOf(character);

            if (of == NotALetter)
            {
                if (inWord)
                {
                    words++;
                    mixedWords += isMixed ? 1 : 0;
                }

                inWord = false;
                alphabet = AnyAlphabet;
                isMixed = false;
                continue;
            }

            inWord = true;

            if (of == AnyAlphabet)
            {
                continue;
            }

            if (alphabet == AnyAlphabet)
            {
                alphabet = of;
            }
            else if (alphabet != of)
            {
                isMixed = true;
            }
        }

        if (inWord)
        {
            words++;
            mixedWords += isMixed ? 1 : 0;
        }

        return mixedWords >= MinMixedWords && mixedWords * WordsPerMixedWord >= words;
    }

    /// <summary>
    /// The alphabet a character is a letter of. Digits and punctuation end a word, so "tomon0première"
    /// is two words and "Москва/Moscow" is two of one alphabet each.
    /// </summary>
    private static int AlphabetOf(char character)
    {
        if (character < '\u0080')
        {
            return char.IsAsciiLetter(character) ? Latin : NotALetter;
        }

        switch (char.GetUnicodeCategory(character))
        {
            // An accent written as a mark of its own, a vowel sign, and the ʻokina of "Hawaiʻi".
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.ModifierLetter:
                return AnyAlphabet;

            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.OtherLetter:
                break;

            default:
                return NotALetter;
        }

        return character switch
        {
            // Latin-1 to Extended-B, and the phonetic letters, where Azerbaijani's ə is.
            < 'ʰ' => Latin,
            >= 'Ͱ' and < 'Ѐ' => Greek,
            >= 'Ѐ' and < '԰' => Cyrillic,
            >= '԰' and < '֐' => Armenian,
            >= '֐' and < '؀' => Hebrew,
            >= '؀' and < '܀' => Arabic,
            >= 'ݐ' and < 'ހ' => Arabic,
            >= 'ࡰ' and < 'ऀ' => Arabic,

            // Written without spaces between words, so other alphabets stand right against them:
            // Thai, Lao, Tibetan, Burmese, Khmer.
            >= '฀' and < 'Ⴀ' => AnyAlphabet,
            >= 'ក' and < '᠀' => AnyAlphabet,

            >= 'Ⴀ' and < 'ᄀ' => Georgian,
            >= 'ᄀ' and < 'ሀ' => AnyAlphabet, // Korean letters on their own
            >= 'ሀ' and < 'Ꭰ' => Ethiopic,
            >= 'Ꭰ' and < '᐀' => Cherokee,
            >= '᐀' and < ' ' => Syllabics, // Nunavut's signs
            >= '᠀' and < 'ᢰ' => Mongolian,
            >= 'ᢰ' and < 'ᤀ' => Syllabics,
            >= 'ᲀ' and < 'Ა' => Cyrillic,
            >= 'Ა' and < '᳀' => Georgian,
            >= 'Ḁ' and < 'ἀ' => Latin, // Vietnamese
            >= 'ἀ' and < ' ' => Greek,
            >= 'Ⱡ' and < 'Ⲁ' => Latin,
            >= 'ⴀ' and < 'ⴰ' => Georgian,
            >= 'ⶀ' and < 'ⷠ' => Ethiopic,
            >= 'ⷠ' and < '⸀' => Cyrillic,

            // Chinese, Japanese and Korean, their half- and full-width forms included.
            >= '⺀' and < 'ꀀ' => AnyAlphabet,
            >= 'Ꙁ' and < 'ꚠ' => Cyrillic,
            >= '꜠' and < 'ꠀ' => Latin,
            >= 'ꥠ' and < 'ꦀ' => AnyAlphabet,
            >= 'ꧠ' and < 'ꨀ' => AnyAlphabet,
            >= 'ꩠ' and < 'ꪀ' => AnyAlphabet,
            >= '꬀' and < 'ꬰ' => Ethiopic,
            >= 'ꬰ' and < 'ꭰ' => Latin,
            >= 'ꭰ' and < 'ꯀ' => Cherokee,
            >= '가' and < '\uD800' => AnyAlphabet,
            >= '豈' and < 'ﬀ' => AnyAlphabet,
            >= 'ﬀ' and < '﬇' => Latin,
            >= 'ﬓ' and < '﬘' => Armenian,
            >= 'יִ' and < 'ﭐ' => Hebrew,
            >= 'ﭐ' and < '︀' => Arabic,
            >= '︰' and < '﹐' => AnyAlphabet,
            >= 'ﹰ' and < '＀' => Arabic,
            >= '＀' => AnyAlphabet,

            _ => FirstUnnamedAlphabet + (character >> 7)
        };
    }
}
