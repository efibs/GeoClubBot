using System.Text.RegularExpressions;
using Configuration;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI.Ingestion;
using UseCases.UseCases.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// Reads a community-maintained guide library published as a Google Sheet, turning each linked
/// resource into a catalogued source.
///
/// Exported as XLSX rather than CSV because the CSV export silently drops every hyperlink target —
/// and the hyperlinks are the data. The visible cell text is only a resource's name.
///
/// Every link is catalogued, including the ones that cannot be indexed, so an operator can see what
/// share of a library the bot actually covers instead of a silently shorter list.
/// </summary>
public sealed partial class MetaLibrarySourceCatalog(
    IHttpClientFactory httpClientFactory,
    IOptions<AiIngestionConfiguration> configuration,
    ILogger<MetaLibrarySourceCatalog> logger) : ISourceCatalog
{
    /// <summary>Header cell that identifies a content sheet and the column holding the links.</summary>
    private const string LinkColumnHeader = "Name/Link";

    private const string CountryColumnHeader = "Country";
    private const string AuthorColumnHeader = "Author";
    private const string TypeColumnHeader = "Type";
    private const string DescriptionColumnHeader = "Description";

    /// <summary>Rows scanned while looking for the header before giving up on a sheet.</summary>
    private const int HeaderSearchDepth = 12;

    public string SourceType => "meta-library";

    public async Task<Result<IReadOnlyList<SourceDescriptor>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var sheetId = configuration.Value.MetaLibrarySheetId;
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            // Not configured is not a failure: the library belongs to someone else and syncing from
            // it is opt-in.
            return Array.Empty<SourceDescriptor>();
        }

        byte[] workbook;
        try
        {
            var client = httpClientFactory.CreateClient(PlonkItSourceExtractor.HttpClientName);
            var endpoint = new Uri($"https://docs.google.com/spreadsheets/d/{sheetId}/export?format=xlsx");

            // Redirects must be followed: the export endpoint answers with one, and not following it
            // returns an empty body rather than an error.
            workbook = await client.GetByteArrayAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogFetchFailed(logger, ex);
            return Error.Unexpected("ai.library_unreachable", "Could not download the guide library.");
        }

        try
        {
            var sources = Parse(workbook);
            LogCatalogued(logger, sources.Count, sources.Count(source => source.UnsupportedReason is null));
            return sources;
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidOperationException or OpenXmlPackageException)
        {
            LogParseFailed(logger, ex);
            return Error.Unexpected("ai.library_unparsable", "Could not read the guide library workbook.");
        }
    }

    private static List<SourceDescriptor> Parse(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);

        var workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException("The workbook has no content.");

        var sharedStrings = ReadSharedStrings(workbookPart);
        var sources = new List<SourceDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sheets = workbookPart.Workbook?.Descendants<Sheet>() ?? [];

        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not { } relationshipId
                || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            // The sheet name carries a running count, e.g. "Africa (87)".
            var continent = SheetCount().Replace(sheet.Name?.Value ?? string.Empty, string.Empty).Trim();

            ReadSheet(worksheetPart, sharedStrings, continent, sources, seen);
        }

        return sources;
    }

    private static void ReadSheet(
        WorksheetPart worksheetPart,
        IReadOnlyList<string> sharedStrings,
        string continent,
        List<SourceDescriptor> sources,
        HashSet<string> seen)
    {
        if (worksheetPart.Worksheet is not { } worksheet)
        {
            return;
        }

        var rows = worksheet.Descendants<Row>().ToList();

        var columns = FindColumns(rows, sharedStrings, out var headerRowIndex);
        if (columns is null)
        {
            // Not a content sheet — the workbook also holds an introduction, an FAQ and a changelog.
            return;
        }

        var hyperlinks = ReadHyperlinks(worksheetPart, worksheet);
        var country = string.Empty;

        foreach (var row in rows.Where(row => row.RowIndex?.Value > headerRowIndex))
        {
            var cells = row.Elements<Cell>()
                .Where(cell => cell.CellReference?.Value is not null)
                .ToDictionary(cell => ColumnOf(cell.CellReference!.Value!), cell => cell, StringComparer.Ordinal);

            // The country is written only on the first row of each block, so it carries downwards.
            if (columns.Country is { } countryColumn
                && cells.TryGetValue(countryColumn, out var countryCell)
                && ReadCell(countryCell, sharedStrings) is { Length: > 0 } countryText)
            {
                country = CleanCountry(countryText);
            }

            if (!cells.TryGetValue(columns.Link, out var linkCell)
                || linkCell.CellReference?.Value is not { } reference
                || !hyperlinks.TryGetValue(reference, out var target)
                || !Uri.TryCreate(target, UriKind.Absolute, out var url))
            {
                continue;
            }

            var classified = SourceLinkClassifier.Classify(url);

            // The same resource is often listed under several countries; keep the first mention.
            if (!seen.Add($"{classified.SourceType}|{classified.NaturalKey}"))
            {
                continue;
            }

            sources.Add(new SourceDescriptor(
                classified.SourceType,
                classified.NaturalKey,
                url,
                Title: ReadCell(linkCell, sharedStrings),
                Country: country.Length == 0 ? null : country,
                Continent: continent.Length == 0 ? null : continent,
                Author: Read(cells, columns.Author, sharedStrings),
                Priority: ReadPriority(Read(cells, columns.Priority, sharedStrings)),
                UnsupportedReason: classified.UnsupportedReason));
        }
    }

    /// <summary>
    /// Locates the header row and maps each column by its label, so the sheet can gain or reorder
    /// columns without silently reading the wrong ones.
    /// </summary>
    private static LibraryColumns? FindColumns(
        IReadOnlyList<Row> rows,
        IReadOnlyList<string> sharedStrings,
        out uint headerRowIndex)
    {
        headerRowIndex = 0;

        foreach (var row in rows.Take(HeaderSearchDepth))
        {
            var headers = row.Elements<Cell>()
                .Where(cell => cell.CellReference?.Value is not null)
                .Select(cell => (Column: ColumnOf(cell.CellReference!.Value!), Text: ReadCell(cell, sharedStrings)))
                .Where(entry => entry.Text.Length > 0)
                .ToList();

            var link = headers.FirstOrDefault(entry =>
                entry.Text.Equals(LinkColumnHeader, StringComparison.OrdinalIgnoreCase));

            if (link.Column is null)
            {
                continue;
            }

            headerRowIndex = row.RowIndex?.Value ?? 0;

            string? ColumnFor(string header) => headers
                .FirstOrDefault(entry => entry.Text.Equals(header, StringComparison.OrdinalIgnoreCase)).Column;

            return new LibraryColumns(
                link.Column,
                ColumnFor(CountryColumnHeader),
                ColumnFor(AuthorColumnHeader),
                ColumnFor(TypeColumnHeader),
                ColumnFor(DescriptionColumnHeader),
                // The priority marker sits in the unlabelled column just before the link.
                PreviousColumn(link.Column));
        }

        return null;
    }

    private static Dictionary<string, string> ReadHyperlinks(WorksheetPart worksheetPart, Worksheet worksheet)
    {
        var targets = worksheetPart.HyperlinkRelationships
            .ToDictionary(relationship => relationship.Id, relationship => relationship.Uri.ToString(), StringComparer.Ordinal);

        var byCell = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var hyperlink in worksheet.Descendants<Hyperlink>())
        {
            if (hyperlink.Id?.Value is not { } id
                || hyperlink.Reference?.Value is not { } reference
                || !targets.TryGetValue(id, out var target))
            {
                continue;
            }

            // A reference can be a range; the first cell is the one carrying the link.
            byCell[reference.Split(':')[0]] = target;
        }

        return byCell;
    }

    private static IReadOnlyList<string> ReadSharedStrings(WorkbookPart workbookPart) =>
        workbookPart.SharedStringTablePart?.SharedStringTable is { } table
            ? [.. table.Elements<SharedStringItem>().Select(item => item.InnerText)]
            : [];

    private static string ReadCell(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index].Trim();
        }

        return value.Trim();
    }

    private static string? Read(
        Dictionary<string, Cell> cells,
        string? column,
        IReadOnlyList<string> sharedStrings)
    {
        if (column is null || !cells.TryGetValue(column, out var cell))
        {
            return null;
        }

        var text = ReadCell(cell, sharedStrings);
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The library grades entries with emoji rather than numbers. Ranking nudges better-regarded
    /// guides upward; an unrecognised marker simply carries no weight.
    /// </summary>
    private static int ReadPriority(string? marker) => marker switch
    {
        null => 0,
        _ when marker.Contains('‼') => 3,
        _ when marker.Contains('❕') => 2,
        _ when marker.Contains('♻') => 1,
        _ => 0
    };

    /// <summary>Country cells carry a running count, e.g. "Bangladesh\n(8)".</summary>
    private static string CleanCountry(string value) =>
        SheetCount().Replace(value.ReplaceLineEndings(" "), string.Empty).Trim();

    private static string ColumnOf(string cellReference) =>
        new(cellReference.TakeWhile(char.IsLetter).ToArray());

    private static string? PreviousColumn(string column) =>
        column.Length == 1 && column[0] > 'A' ? ((char)(column[0] - 1)).ToString() : null;

    private sealed record LibraryColumns(
        string Link,
        string? Country,
        string? Author,
        string? Type,
        string? Description,
        string? Priority);

    [GeneratedRegex(@"\(\s*\d+\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex SheetCount();

    [LoggerMessage(LogLevel.Information, "Catalogued {TotalCount} library entries, {SupportedCount} of them indexable.")]
    static partial void LogCatalogued(ILogger logger, int totalCount, int supportedCount);

    [LoggerMessage(LogLevel.Warning, "Could not download the guide library.")]
    static partial void LogFetchFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Could not read the guide library workbook.")]
    static partial void LogParseFailed(ILogger logger, Exception exception);
}
