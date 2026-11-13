using System.Globalization;
using System.Text;

namespace UseCases.UseCases.AI.KnowledgeGaps;

/// <summary>The report as Discord text. Pure, so the layout is pinned by a test.</summary>
public static class KnowledgeGapReportText
{
    /// <summary>Longest example question shown.</summary>
    private const int MaxExampleLength = 140;

    public static string Render(KnowledgeGapReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        text.Append("📉 **Knowledge gaps — last ").Append(report.Days.ToString(CultureInfo.InvariantCulture)).AppendLine(" days**");
        text.Append(report.WeakRetrievals.ToString(CultureInfo.InvariantCulture)).Append(" question(s) with a weak retrieval, ")
            .Append(report.Downrated.ToString(CultureInfo.InvariantCulture)).AppendLine(" answer(s) rated 👎.");

        if (report.Groups.Count == 0)
        {
            text.AppendLine().Append("Nothing to report: every question found guides to answer from.");
            return text.ToString();
        }

        text.AppendLine();
        foreach (var group in report.Groups)
        {
            var country = group.Country is null ? "No country named" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Country);
            text.Append("**").Append(country).Append("**");
            if (group.Topic is not null)
            {
                text.Append(" · ").Append(group.Topic);
            }

            var parts = new List<string>();
            Add(parts, group.NothingFromTheCountry, "nothing from the country");
            Add(parts, group.NoExcerpts, "nothing found");
            Add(parts, group.NothingCited, "nothing cited");
            Add(parts, group.Downrated, "rated 👎");
            text.Append(" — ").AppendLine(string.Join(", ", parts));

            foreach (var example in group.Examples)
            {
                text.Append("-# “").Append(Shorten(example)).AppendLine("”");
            }
        }

        text.AppendLine().Append("-# *Nothing from the country* is the clearest sign a guide is missing; *nothing cited* that the guides found do not answer it.");
        return text.ToString();
    }

    private static void Add(List<string> parts, int count, string label)
    {
        if (count > 0)
        {
            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} {label}");
        }
    }

    private static string Shorten(string text)
    {
        var flat = string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).Replace("”", "\"", StringComparison.Ordinal);
        return flat.Length <= MaxExampleLength ? flat : $"{flat[..(MaxExampleLength - 1)].TrimEnd()}…";
    }
}
