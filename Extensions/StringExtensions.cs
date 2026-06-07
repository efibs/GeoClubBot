using System.Text;

namespace Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Converts an ISO 3166-1 alpha-2 country code (e.g. "DE") into its
    /// regional-indicator flag emoji (e.g. 🇩🇪). Returns <see cref="string.Empty"/>
    /// for null, blank, or non two-letter input.
    /// </summary>
    public static string ToFlagEmoji(this string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return string.Empty;

        var code = countryCode.Trim().ToUpperInvariant();
        if (code.Length != 2 || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1]))
            return string.Empty;

        return string.Concat(code.Select(c => char.ConvertFromUtf32(0x1F1E6 + (c - 'A'))));
    }

    public static List<string> SplitAtCharWithLimit(this string str, string splitChar, int limit)
    {
        var parts = str.Split(splitChar);
        var result = new List<string>();
        var current = new StringBuilder();

        foreach (var part in parts)
        {
            // +1 to account for the splitChar we’ll reinsert (except at the start)
            if (current.Length + part.Length + 1 > limit)
            {
                // save the current chunk and start a new one
                if (current.Length > 0)
                    result.Add(current.ToString().TrimEnd());

                current.Clear();
            }

            if (current.Length > 0)
                current.Append(splitChar);

            current.Append(part);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
