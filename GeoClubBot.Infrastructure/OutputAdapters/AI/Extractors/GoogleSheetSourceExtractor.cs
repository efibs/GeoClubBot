using System.Text;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads publicly shared Google Sheets, which the library uses for tabular metas — area codes by
/// region, plate series by state, and similar lookup tables.
///
/// Rows are grouped rather than indexed individually: a single row of a lookup table carries almost
/// no retrievable signal on its own, while a block of them reads as a coherent answer.
/// </summary>
public sealed class GoogleSheetSourceExtractor(IHttpClientFactory httpClientFactory) : ISourceExtractor
{
    /// <summary>Rows per chunk, each carrying the header so the values stay interpretable.</summary>
    private const int RowsPerChunk = 15;

    public string SourceType => SourceLinkClassifier.GoogleSheet;

    public bool CanHandle(Uri url) =>
        SourceLinkClassifier.Classify(url).SourceType == SourceLinkClassifier.GoogleSheet;

    public async Task<Result<ExtractedDocument>> ExtractAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(
            $"https://docs.google.com/spreadsheets/d/{source.NaturalKey}/gviz/tq?tqx=out:csv");

        string csv;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode is 401 or 403 or 404
                    ? Error.Validation("ai.document_not_public",
                        $"This sheet is not publicly readable (HTTP {(int)response.StatusCode}).")
                    : Error.Unexpected("ai.source_unreachable",
                        $"Could not fetch the sheet (HTTP {(int)response.StatusCode}).");
            }

            csv = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Unexpected("ai.source_unreachable", $"Could not fetch the sheet {source.NaturalKey}.");
        }

        var rows = CsvReader.Read(csv);
        if (rows.Count <= 1)
        {
            return Error.Validation("ai.document_empty", "This sheet has no readable rows.");
        }

        var header = string.Join(" | ", rows[0]);
        var chunks = new List<ExtractedChunk>();

        for (var start = 1; start < rows.Count; start += RowsPerChunk)
        {
            var block = rows.Skip(start).Take(RowsPerChunk)
                .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                .Select(row => string.Join(" | ", row));

            var body = string.Join("\n", block);
            if (body.Length == 0)
            {
                continue;
            }

            chunks.Add(new ExtractedChunk(
                LocalKey: $"r{start}",
                SectionPath: source.Title ?? source.Country ?? "Spreadsheet",
                Text: $"{header}\n{body}"));
        }

        return chunks.Count == 0
            ? Error.Validation("ai.document_empty", "This sheet has no readable rows.")
            : new ExtractedDocument(source.Title, SourceUpdatedAtUtc: null, chunks);
    }
}

/// <summary>
/// Minimal RFC 4180 reader. Guide sheets routinely contain commas and line breaks inside quoted
/// description cells, which a naive split on commas would tear apart mid-sentence.
/// </summary>
internal static class CsvReader
{
    public static List<List<string>> Read(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];

            if (inQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    // Doubled quote inside a quoted field is a literal quote.
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
