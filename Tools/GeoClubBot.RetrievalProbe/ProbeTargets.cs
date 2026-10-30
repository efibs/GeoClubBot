using System.Text.Json;
using System.Text.RegularExpressions;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.RetrievalProbe;

/// <summary>What counts as a good excerpt for one question.</summary>
/// <param name="Key">Words of the question that pick this target.</param>
/// <param name="OnTarget">An excerpt whose text matches is about what the question asks.</param>
/// <param name="Countries">Countries the question is about, as the index spells them; empty when it spans many.</param>
/// <param name="Chunks">Particular excerpts that answer it, each matched against its source URL and text.</param>
/// <param name="Screenshot">
/// The source URL of an indexed picture that stands in for the question's screenshot, whose Discord
/// link has expired by the time an export is replayed. Searched by its stored pixels and left out of
/// every result list, since the user's own screenshot is never in the index.
/// </param>
public sealed record QuestionTarget(
    string Key,
    Regex OnTarget,
    IReadOnlyList<string> Countries,
    IReadOnlyList<(string Label, Regex Pattern)> Chunks,
    string? Screenshot = null)
{
    public bool IsChunk(KnowledgeHit hit, Regex pattern) => pattern.IsMatch($"{hit.SourceUrl}\n{hit.Text}");

    /// <summary>From one of the question's countries; always false when it names none.</summary>
    public bool IsInCountry(KnowledgeHit hit) => hit.Country is { } country && Countries.Contains(country);

    /// <summary>
    /// About what was asked, where it was asked: a red-soil tip from Thailand does not answer "is red
    /// soil in Malaysia regional?". A tester said as much: "I don't care about other countries if I ask
    /// about regionguessing Australia."
    /// </summary>
    public bool IsOnTarget(KnowledgeHit hit) => OnTarget.IsMatch(hit.Text) && (Countries.Count == 0 || IsInCountry(hit));
}

/// <summary>
/// Reads a targets file. Each key is words that occur in a question; the value is either a regex an
/// on-target excerpt matches, or an object that also names the question's countries and the very
/// excerpts that answer it:
/// <code>
/// { "khasi": "khasi|kesiya",
///   "what about corn": { "match": "corn|maize", "countries": ["philippines"], "chunks": ["philippines#9hUv"] },
///   "bollard a meta in australia": { "match": "bollard", "countries": ["australia"],
///                                    "screenshot": "https://www.plonkit.net/australia#…" } }
/// </code>
/// </summary>
public static class ProbeTargets
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<QuestionTarget> Read(string? path)
    {
        if (path is null)
        {
            return [];
        }

        if (!File.Exists(path))
        {
            throw new ProbeException($"No targets file at '{path}'.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            // Checked up front, so a typo in the tenth pattern does not surface after nine questions.
            return [.. document.RootElement.EnumerateObject().Select(Parse)];
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ProbeException(
                "The targets file must be a JSON object of { \"words in the question\": \"regex\" } or "
                + $"{{ \"words\": {{ \"match\": \"regex\", \"countries\": [...], \"chunks\": [...] }} }}: {ex.Message}");
        }
    }

    /// <summary>The first target whose words appear in the question.</summary>
    public static QuestionTarget? For(IReadOnlyList<QuestionTarget> targets, string question) =>
        targets.FirstOrDefault(target => question.Contains(target.Key, StringComparison.OrdinalIgnoreCase));

    private static QuestionTarget Parse(JsonProperty entry)
    {
        if (entry.Value.ValueKind == JsonValueKind.String)
        {
            return new QuestionTarget(entry.Name, Pattern(entry.Value.GetString()!), [], []);
        }

        var value = entry.Value;
        return new QuestionTarget(
            entry.Name,
            Pattern(value.GetProperty("match").GetString()!),
            value.TryGetProperty("countries", out var countries)
                ? [.. countries.EnumerateArray().Select(country => country.GetString()!.ToLowerInvariant())]
                : [],
            value.TryGetProperty("chunks", out var chunks)
                ? [.. chunks.EnumerateArray().Select(chunk => (chunk.GetString()!, Pattern(chunk.GetString()!)))]
                : [],
            value.TryGetProperty("screenshot", out var screenshot) ? screenshot.GetString() : null);
    }

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
}
