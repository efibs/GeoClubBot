using System.Text;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>A problem the user can fix — a missing file, an unreachable index — reported without a stack trace.</summary>
public sealed class ProbeException(string message) : Exception(message);

/// <summary>Markdown written to the console as it is produced, and kept for <c>--out</c>.</summary>
public sealed class ProbeReport
{
    private readonly StringBuilder _text = new();

    public void Line(string line = "")
    {
        Console.WriteLine(line);
        _text.AppendLine(line);
    }

    public override string ToString() => _text.ToString();
}

/// <summary>How the reports describe an excerpt.</summary>
public static class HitDescriptions
{
    /// <summary>
    /// A picture whose only text is its guide's title or section heading: what an extractor falls
    /// back to for a picture with no caption. The model can say nothing about one.
    /// </summary>
    public static bool IsCaptionOnly(KnowledgeHit hit)
    {
        if (hit.Kind != KnowledgeChunkKind.Image)
        {
            return false;
        }

        var text = Collapse(hit.Text);
        return text.Length < 25
               || text.StartsWith("http", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, hit.Title, StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, hit.SectionPath, StringComparison.OrdinalIgnoreCase)
               || string.Equals(text.Replace(" — ", " > ", StringComparison.Ordinal), hit.SectionPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A guide name a reader recognises, linked to the excerpt's source.</summary>
    public static string GuideLink(KnowledgeHit hit)
    {
        var label = Cell(hit.SectionPath ?? hit.Title ?? hit.SourceUrl, 60).Replace("[", "(").Replace("]", ")");
        return $"[{label}]({hit.SourceUrl})";
    }

    /// <summary>Text fit for one markdown table cell: one line, no pipes, cut to length.</summary>
    public static string Cell(string text, int length)
    {
        var collapsed = Collapse(text).Replace("|", "/", StringComparison.Ordinal);
        return collapsed.Length <= length ? collapsed : $"{collapsed[..(length - 1)]}…";
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
}
