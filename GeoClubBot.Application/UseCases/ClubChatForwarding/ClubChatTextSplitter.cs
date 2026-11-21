using System.Globalization;
using Utilities;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>
/// Cuts a text into chat messages GeoGuessr won't change. GeoGuessr silently drops whatever goes
/// past its length limit and turns every line break past its line-break limit into a space, so a
/// longer text is split into parts numbered <c>(1/3) …</c> (the numbering counts toward the length),
/// at a paragraph break if one is near enough, else a line or sentence end, else a space, and only
/// as a last resort in the middle of a word.
/// </summary>
public static class ClubChatTextSplitter
{
    /// <param name="maxLineBreaks">How many line breaks one message keeps; a blank line is two.</param>
    public static Result<IReadOnlyList<string>> Split(string text, int maxLength, int maxParts, int maxLineBreaks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParts, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLineBreaks);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Error.Validation("club-chat.empty", "There is nothing to send.");
        }

        if (trimmed.Length <= maxLength && LineBreaks(trimmed) <= maxLineBreaks)
        {
            return Result<IReadOnlyList<string>>.Success([trimmed]);
        }

        // The prefix's width depends on how many parts there are, which depends on the prefix:
        // guess, and widen until the count fits the digits it was split for.
        var assumed = 2;
        List<string> chunks;
        while (true)
        {
            chunks = Chunk(trimmed, maxLength - Prefix(assumed, assumed).Length, maxLineBreaks);
            if (Digits(chunks.Count) <= Digits(assumed))
            {
                break;
            }

            assumed = chunks.Count;
        }

        if (chunks.Count > maxParts)
        {
            var allowed = maxParts * (maxLength - Prefix(maxParts, maxParts).Length);
            var excess = trimmed.Length - allowed;
            return Error.Validation(
                "club-chat.too_long",
                $"That text needs {chunks.Count} chat messages, but at most {maxParts} are allowed "
                + $"(GeoGuessr cuts each at {maxLength} characters and keeps at most {maxLineBreaks} line breaks in one). "
                + (excess > 0 ? $"Shorten it by about {excess} characters." : "Remove some line breaks or blank lines."));
        }

        return Result<IReadOnlyList<string>>.Success(chunks.Select((chunk, index) => Prefix(index + 1, chunks.Count) + chunk).ToList());
    }

    public static string Prefix(int part, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"({part}/{count}) ");

    private static List<string> Chunk(string text, int budget, int maxLineBreaks)
    {
        var chunks = new List<string>();
        var remaining = text;
        while (remaining.Length > budget || LineBreaks(remaining) > maxLineBreaks)
        {
            var cut = CutAt(remaining, Limit(remaining, budget, maxLineBreaks));
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

    /// <summary>
    /// The most of <paramref name="text"/> one message can take: <paramref name="budget"/> characters,
    /// or up to the line break one past the limit if that comes first. The character at the limit is
    /// still looked at, so a boundary right there can be cut at.
    /// </summary>
    private static int Limit(string text, int budget, int maxLineBreaks)
    {
        var breaks = 0;
        for (var i = 0; i < text.Length && i < budget; i++)
        {
            if (text[i] == '\n' && ++breaks > maxLineBreaks)
            {
                return i;
            }
        }

        return budget;
    }

    /// <summary>Where to end the next chunk, at most <paramref name="limit"/>: the length of the text that goes into it.</summary>
    private static int CutAt(string text, int limit)
    {
        // A boundary in the first third would leave a stub of a message; a nearer one is better.
        var earliest = limit / 3;
        var window = text.AsSpan(0, Math.Min(limit + 1, text.Length));

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
        return char.IsHighSurrogate(text[limit - 1]) && limit > 1 ? limit - 1 : limit;
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

    private static int LineBreaks(string text) => text.AsSpan().Count('\n');

    private static int Digits(int value) => value.ToString(CultureInfo.InvariantCulture).Length;
}
