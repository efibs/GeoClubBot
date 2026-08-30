using System.Net;
using Configuration;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Extractors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI.Ingestion;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Builds a workbook shaped like the real guide library — a header row below some preamble, a country
/// written once per block, links carried as hyperlink relationships rather than cell text — and reads
/// it back. That shape is exactly why the CSV export is unusable: it drops the hyperlinks entirely.
/// </summary>
public sealed class MetaLibrarySourceCatalogTests
{
    [Fact]
    public async Task List_ReadsEveryLinkedResource()
    {
        var sources = await ListAsync(
            Row("Bangladesh\n(3)", "‼️", "Plonk It Guide", "https://www.plonkit.net/bangladesh", "Plonk It team"),
            Row(null, "❕", "District names", "https://imgur.com/a/e3RTN2O", "@bagaboiebailey"),
            Row(null, "♻️", "The Bangladesh Doc", "https://docs.google.com/document/d/abc123/edit", "@itsraduc"));

        sources.Should().HaveCount(3);
        sources.Select(source => source.SourceType).Should().Equal("plonkit", "imgur", "gdoc");
        sources.Select(source => source.NaturalKey).Should().Equal("bangladesh", "e3RTN2O", "abc123");
    }

    [Fact]
    public async Task List_CarriesTheCountryDownAcrossItsBlock()
    {
        // The country is written only on the first row of each block; without forward-filling, every
        // row but the first would lose the single most useful filter the library provides.
        var sources = await ListAsync(
            Row("Bangladesh\n(2)", "‼️", "First", "https://www.plonkit.net/bangladesh", null),
            Row(null, "❕", "Second", "https://imgur.com/a/aaa111", null),
            Row("Bhutan\n(1)", "‼️", "Third", "https://www.plonkit.net/bhutan", null));

        sources.Select(source => source.Country).Should().Equal("Bangladesh", "Bangladesh", "Bhutan");
    }

    [Fact]
    public async Task List_ReadsTheContinentFromTheSheetName()
    {
        var sources = await ListAsync(Row("Bangladesh\n(1)", "‼️", "Guide", "https://www.plonkit.net/bangladesh", null));

        sources.Should().ContainSingle().Which.Continent.Should().Be("Asia");
    }

    [Fact]
    public async Task List_TranslatesTheEmojiGradeIntoAPriority()
    {
        var sources = await ListAsync(
            Row("A\n(3)", "‼️", "Highest", "https://www.plonkit.net/a", null),
            Row(null, "❕", "Middle", "https://www.plonkit.net/b", null),
            Row(null, "♻️", "Lowest", "https://www.plonkit.net/c", null));

        sources.Select(source => source.Priority).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task List_RecordsUnindexableEntriesWithAReason()
    {
        // A library is a sixth Discord links and a twentieth videos. Dropping them would make a
        // partially covered library look complete.
        var sources = await ListAsync(
            Row("A\n(2)", "‼️", "A video", "https://www.youtube.com/watch?v=abc", null),
            Row(null, "❕", "A chat link", "https://discord.com/channels/1/2/3", null));

        sources.Should().HaveCount(2);
        sources.Should().OnlyContain(source => source.UnsupportedReason != null);
        sources.Should().OnlyContain(source => source.SourceType == "unsupported");
    }

    [Fact]
    public async Task List_KeepsOnlyTheFirstMentionOfAResource()
    {
        // Guides covering several countries are listed under each of them.
        var sources = await ListAsync(
            Row("A\n(1)", "‼️", "Shared guide", "https://docs.google.com/document/d/shared/edit", null),
            Row("B\n(1)", "‼️", "Shared guide", "https://docs.google.com/document/d/shared/edit", null));

        sources.Should().ContainSingle();
        sources[0].Country.Should().Be("A", "the first mention wins");
    }

    [Fact]
    public async Task List_IgnoresSheetsThatAreNotResourceListings()
    {
        // The workbook also holds an introduction, an FAQ and a changelog; the header row is what
        // identifies a listing, so those are skipped without needing to be named.
        var workbook = BuildWorkbook(("Introduction", null), ("Asia (1)",
            [Row("A\n(1)", "‼️", "Guide", "https://www.plonkit.net/a", null)]));

        var sources = await ListWorkbookAsync(workbook);

        sources.Should().ContainSingle();
    }

    [Fact]
    public async Task List_ReturnsNothing_WhenNoLibraryIsConfigured()
    {
        // Syncing from someone else's library is opt-in, so an unset id is not an error.
        var catalog = CreateCatalog(BuildWorkbook(("Asia (1)", [])), sheetId: null);

        var result = await catalog.ListAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private static async Task<IReadOnlyList<SourceDescriptor>> ListAsync(params RowSpec[] rows) =>
        await ListWorkbookAsync(BuildWorkbook(("Asia (1)", rows)));

    private static async Task<IReadOnlyList<SourceDescriptor>> ListWorkbookAsync(byte[] workbook)
    {
        var result = await CreateCatalog(workbook, "sheet-id").ListAsync();
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static MetaLibrarySourceCatalog CreateCatalog(byte[] workbook, string? sheetId)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(PlonkItSourceExtractor.HttpClientName)
            .Returns(new HttpClient(new WorkbookHandler(workbook)));

        return new MetaLibrarySourceCatalog(
            factory,
            Options.Create(new AiIngestionConfiguration { MetaLibrarySheetId = sheetId }),
            NullLogger<MetaLibrarySourceCatalog>.Instance);
    }

    private static RowSpec Row(string? country, string? grade, string title, string url, string? author) =>
        new(country, grade, title, url, author);

    private sealed record RowSpec(string? Country, string? Grade, string Title, string Url, string? Author);

    /// <summary>
    /// Writes a workbook in the same shape as the published library: two preamble rows, then a header,
    /// then resource rows whose links live in hyperlink relationships rather than in the cells.
    /// </summary>
    private static byte[] BuildWorkbook(params (string Name, RowSpec[]? Rows)[] sheets)
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheetElements = new Sheets();
            uint sheetId = 1;

            foreach (var (name, rows) in sheets)
            {
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                var hyperlinks = new Hyperlinks();

                sheetData.Append(TextRow(1, ("A", "Some preamble")));

                if (rows is not null)
                {
                    sheetData.Append(TextRow(3,
                        ("A", "Country"), ("D", "Name/Link"), ("E", "Author"), ("F", "Type"), ("G", "Description")));

                    uint rowIndex = 4;
                    foreach (var row in rows)
                    {
                        sheetData.Append(TextRow(rowIndex,
                            ("A", row.Country), ("C", row.Grade), ("D", row.Title), ("E", row.Author)));

                        var relationshipId = worksheetPart.AddHyperlinkRelationship(new Uri(row.Url), true).Id;
                        hyperlinks.Append(new Hyperlink { Reference = $"D{rowIndex}", Id = relationshipId });
                        rowIndex++;
                    }
                }

                var worksheet = new Worksheet(sheetData);
                if (hyperlinks.ChildElements.Count > 0)
                {
                    worksheet.Append(hyperlinks);
                }

                worksheetPart.Worksheet = worksheet;

                sheetElements.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId++,
                    Name = name
                });
            }

            workbookPart.Workbook.Append(sheetElements);
        }

        return stream.ToArray();
    }

    private static Row TextRow(uint rowIndex, params (string Column, string? Value)[] cells)
    {
        var row = new Row { RowIndex = rowIndex };

        foreach (var (column, value) in cells.Where(cell => cell.Value is not null))
        {
            row.Append(new Cell
            {
                CellReference = $"{column}{rowIndex}",
                DataType = CellValues.String,
                CellValue = new CellValue(value!)
            });
        }

        return row;
    }

    private sealed class WorkbookHandler(byte[] workbook) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(workbook),
                RequestMessage = request
            });
    }
}
