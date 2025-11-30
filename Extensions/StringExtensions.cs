using System.Text;

namespace Extensions;

public static class StringExtensions
{
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