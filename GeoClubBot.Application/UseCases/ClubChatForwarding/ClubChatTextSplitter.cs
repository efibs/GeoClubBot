using System.Globalization;
using Utilities;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>
/// Cuts a text into chat messages GeoGuessr won't shorten. GeoGuessr silently drops whatever goes
/// past its limit, so a longer text is split into parts numbered <c>(1/3) …</c> (the numbering
/// counts toward the limit), at a paragraph break if one is near enough, else a line or sentence
/// end, else a space, and only as a last resort in the middle of a word.
/// </summary>
public static class ClubChatTextSplitter
{
    public static Result<IReadOnlyList<string>> Split(string text, int maxLength, int maxParts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParts, 1);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Error.Validation("club-chat.empty", "There is nothing to send.");
        }

        if (trimmed.Length <= maxLength)
        {
            return Result<IReadOnlyList<string>>.Success([trimmed]);
        }

        // The prefix's width depends on how many parts there are, which depends on the prefix:
        // guess, and widen until the count fits the digits it was split for.
        var assumed = 2;
        List<string> chunks;
        while (true)
        {
            chunks = Chunk(trimmed, maxLength - Prefix(assumed, assumed).Length);
            if (Digits(chunks.Count) <= Digits(assumed))
            {
                break;
            }

            assumed = chunks.Count;
        }

        if (chunks.Count > maxParts)
        {
            var allowed = maxParts * (maxLength - Prefix(maxParts, maxParts).Length);
            return Error.Validation(
                "club-chat.too_long",
                $"That text needs {chunks.Count} chat messages, but at most {maxParts} are allowed "
                + $"(GeoGuessr cuts each at {maxLength} characters). Shorten it by about {Math.Max(1, trimmed.Length - allowed)} characters.");
        }

        return Result<IReadOnlyList<string>>.Success(chunks.Select((chunk, index) => Prefix(index + 1, chunks.Count) + chunk).ToList());
    }

    public static string Prefix(int part, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"({part}/{count}) ");

    private static List<string> Chunk(string text, int budget)
    {
        var chunks = new List<string>();
        var remaining = text;
        while (remaining.Length > budget)
        {
            var cut = CutAt(remaining, budget);
            var chunk = remaining[..cut].TrimEnd();
            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            remaining = remaining[cut..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            chunks.Add(remaining);
        }

        return chunks;
    }

    /// <summary>Where to end the next chunk: the length of the text that goes into it.</summary>
    private static int CutAt(string text, int budget)
    {
        // A boundary in the first third would leave a stub of a message; a nearer one is better.
        var earliest = budget / 3;
        var window = text.AsSpan(0, budget + 1);

        var paragraph = window.LastIndexOf("\n\n");
        if (paragraph >= earliest)
        {
            return paragraph;
        }

        var line = window.LastIndexOf('\n');
        if (line >= earliest)
        {
            return line;
        }

        var sentence = LastSentenceEnd(window);
        if (sentence >= earliest)
        {
            return sentence;
        }

        var space = window.LastIndexOfAny(' ', '\t');
        if (space > 0)
        {
            return space;
        }

        // In the middle of a word, but never between the halves of a surrogate pair.
        return char.IsHighSurrogate(text[budget - 1]) ? budget - 1 : budget;
    }

    /// <summary>The position just after the last <c>.</c>, <c>!</c> or <c>?</c> followed by a space, or -1.</summary>
    private static int LastSentenceEnd(ReadOnlySpan<char> window)
    {
        for (var i = window.Length - 2; i > 0; i--)
        {
            if (window[i + 1] == ' ' && window[i] is '.' or '!' or '?')
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static int Digits(int value) => value.ToString(CultureInfo.InvariantCulture).Length;
}
