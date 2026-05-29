using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI;

/// <summary>
/// Pulls the PlonkIt guides index from plonkit.net/api/guides, then walks every country
/// page with Puppeteer to extract section divs. Emits one section event per div plus
/// status events suitable for streaming back through the rebuild progress channel.
/// </summary>
public sealed partial class PuppeteerPlonkItPageFetcher(ILogger<PuppeteerPlonkItPageFetcher> logger)
    : IPlonkItPageFetcher, IAsyncDisposable
{
    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    public async IAsyncEnumerable<PlonkItFetchEvent> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new PlonkItStatusEvent("Reading guides...");

        using var httpClient = new HttpClient();
        var guides = await httpClient
            .GetFromJsonAsync<PlonkItGuideResponse>("https://www.plonkit.net/api/guides", cancellationToken)
            .ConfigureAwait(false);

        if (guides == null)
        {
            LogPlonkItGuideRetrievalFailed(logger);
            yield return new PlonkItStatusEvent("ERROR: PlonkItGuide could not be retrieved.");
            yield break;
        }

        var idx = 0;
        foreach (var guide in guides.Data)
        {
            var prefix = $"[{idx++ * 100.0M / guides.Data.Count:F2}%] Fetching '{guide.Title}': ";

            await foreach (var evt in FetchCountryPageAsync($"https://www.plonkit.net/{guide.Slug}", guide.Title, guide.Cat, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (evt is PlonkItStatusEvent status)
                {
                    yield return new PlonkItStatusEvent(prefix + status.Message);
                }
                else
                {
                    yield return evt;
                }
            }
        }

        yield return new PlonkItStatusEvent("Initializing PlonkIt Guide done.");
    }

    private async IAsyncEnumerable<PlonkItFetchEvent> FetchCountryPageAsync(
        string url,
        string guideTitle,
        IReadOnlyList<string> continents,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlonkItStatusEvent("Fetching Page...");

        var pageContent = await FetchPageContentsAsync(url).ConfigureAwait(false);
        if (pageContent == null)
        {
            yield return new PlonkItStatusEvent("ERROR: Page could not be retrieved.");
            yield break;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(pageContent);

        var idPattern = IdRegex();
        var divs = doc.DocumentNode
            .SelectNodes("//div[@id]")
            ?.Where(div => idPattern.IsMatch(div.GetAttributeValue("id", "")))
            .ToList();

        if (divs is null || divs.Count == 0)
        {
            yield return new PlonkItStatusEvent("ERROR: No sections found.");
            yield break;
        }

        var country = url.Split('/').Last();

        for (var i = 0; i < divs.Count; i++)
        {
            var div = divs[i];
            var innerDiv = div.SelectNodes("./div/div");
            var content = innerDiv?.FirstOrDefault()?.InnerHtml;
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            var id = div.Attributes["id"].Value;
            var source = $"{url}#{id}";

            yield return new PlonkItStatusEvent($"[{i * 100.0M / divs.Count:F2}%] Parsing section #{id}...");

            yield return new PlonkItSectionEvent(new PlonkItSection(
                Country: country,
                GuideTitle: guideTitle,
                Source: source,
                InnerHtml: content,
                InnerText: innerDiv!.First().InnerText,
                Continents: continents));
        }
    }

    private async Task<string?> FetchPageContentsAsync(string url)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var browser = await GetBrowserAsync().ConfigureAwait(false);
                var page = await browser.NewPageAsync().ConfigureAwait(false);

                await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle2],
                    Timeout = 30000
                }).ConfigureAwait(false);

                await Task.Delay(2000).ConfigureAwait(false);

                var content = await page.GetContentAsync().ConfigureAwait(false);
                await page.CloseAsync().ConfigureAwait(false);

                return content;
            }
            catch (Exception ex)
            {
                LogErrorFetchingPlonkItGuidePageUrl(logger, ex, url);
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null) return _browser;

        await _browserLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null) return _browser;

            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync().ConfigureAwait(false);

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox"]
            }).ConfigureAwait(false);

            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
            _browser = null;
        }
        _browserLock.Dispose();
    }

    [GeneratedRegex(@"^\d+-\d+$")]
    private static partial Regex IdRegex();

    [LoggerMessage(LogLevel.Error, "Error fetching plonk-it-guide page {url}")]
    static partial void LogErrorFetchingPlonkItGuidePageUrl(ILogger<PuppeteerPlonkItPageFetcher> logger, Exception ex, string url);

    [LoggerMessage(LogLevel.Error, "PlonkItGuide could not be retrieved.")]
    static partial void LogPlonkItGuideRetrievalFailed(ILogger<PuppeteerPlonkItPageFetcher> logger);

    private sealed class PlonkItGuide
    {
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public List<string> Cat { get; set; } = [];
    }

    private sealed class PlonkItGuideResponse
    {
        public List<PlonkItGuide> Data { get; set; } = [];
    }
}
