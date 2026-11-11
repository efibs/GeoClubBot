using System.Globalization;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Ingestion;
using UseCases.UseCases.AI.Retrieval;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// How much of a guide PlonkIt has too: for every chunk of the guide, the PlonkIt tip of the same
/// country whose text is nearest, and how near. PlonkIt is the guide most players have read, so a clue
/// it also carries is one a member "already knows"; the bot marks such excerpts for a question that
/// asks for something new. This is where the similarity that counts as "the same clue" is read off
/// real pairs instead of guessed.
///
/// Searches by the chunks' own stored vectors: nothing is embedded, so it needs no key.
/// </summary>
public static class OverlapCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        var source = context.Arguments.Source
                     ?? throw new ProbeException("overlap needs --source <guide link or type:key>, e.g. --source gdoc:<id>");

        var (sourceType, sourceKey) = await ChunksCommand.ResolveGuideAsync(context, source).ConfigureAwait(false);
        var chunks = await context.Index.ReadSourceChunksAsync(sourceType, sourceKey, 5_000, context.CancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
        {
            throw new ProbeException($"The index holds no chunk of {sourceType}:{sourceKey}.");
        }

        // Compared within the country however each guide spells it: a document's "usa" is PlonkIt's "united states".
        var gazetteer = CountryGazetteer.Build(await context.Index.ListCountriesAsync(context.CancellationToken).ConfigureAwait(false));
        var spellings = chunks
            .Select(chunk => chunk.Country)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                country => country,
                country => gazetteer.Resolve(country) is [var one] ? one.IndexNames : (IReadOnlyList<string>)[country],
                StringComparer.Ordinal);

        var nearest = await context.Index
            .ReadNearestOfSourceAsync(chunks, SourceLinkClassifier.PlonkIt, spellings, context.CancellationToken)
            .ConfigureAwait(false);

        var pairs = chunks
            .Where(chunk => nearest.ContainsKey(chunk.Id))
            .Select(chunk => (Chunk: chunk, Nearest: nearest[chunk.Id]))
            .OrderByDescending(pair => pair.Nearest.Score)
            .ToList();

        var report = context.Report;
        report.Line($"# What PlonkIt has of {sourceType}:{sourceKey}");
        report.Line();
        report.Line($"{chunks.Count} chunk(s), {pairs.Count} with a PlonkIt tip of the same country to compare with.");
        report.Line();
        report.Line("By similarity: " + string.Join(", ", new[] { 0.9, 0.85, 0.8, 0.75, 0.7, 0.65, 0.6 }
            .Select(bar => $"≥{bar.ToString("0.00", CultureInfo.InvariantCulture)}: {pairs.Count(pair => pair.Nearest.Score >= bar)}")));
        report.Line();
        report.Line("| similarity | the guide's chunk | PlonkIt's nearest tip |");
        report.Line("|---|---|---|");

        foreach (var (chunk, tip) in pairs.Take(context.Arguments.Max))
        {
            report.Line($"| {tip.Score.ToString("0.000", CultureInfo.InvariantCulture)} | {HitDescriptions.Cell(chunk.Text, 150)} | "
                        + $"{HitDescriptions.Cell(tip.Text, 150)} |");
        }
    }
}
