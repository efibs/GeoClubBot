using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Constants;
using HtmlAgilityPack;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.InputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace UseCases.UseCases.AI;

public class SectionRecord
{
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public partial class PlonkItGuideVectorStore(
    QdrantClient client,
    VllmEmbeddingService embeddingService,
    IGetPlonkItGuideSectionEmbeddingTextUseCase getPlonkItGuideSectionEmbeddingTextUseCase,
    ILogger<PlonkItGuideVectorStore> logger,
    IConfiguration config,
    string collectionName = "plonkit-guide")
{
    private const int VectorSize = 1024;

    public SemaphoreSlim RebuildStoreLock => _rebuildStoreLock;

    #region Management methods
    
    public async Task InitializeAsync()
    {
        // Lock
        await _rebuildStoreLock.WaitAsync().ConfigureAwait(false);

        try
        {
            // Check if collection exists
            var collections = await client.ListCollectionsAsync().ConfigureAwait(false);
            var exists = collections.Any(c => c == collectionName);

            if (!exists)
            {
                // Test the connection
                var connectionExists = await _testConnectionsAsync().ConfigureAwait(false);
        
                // If the connection is not there
                if (connectionExists == false)
                {
                    // Log error
                    logger.LogError("Could not create PlonkIt Guide vector store because not all connections could be established.");
                    return;
                }
                
                // Create collection
                await client.CreateCollectionAsync(
                    collectionName: collectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = VectorSize,
                        Distance = Distance.Cosine
                    }
                ).ConfigureAwait(false);

                await foreach (var statusUpdate in _initPlonkItStoreAsync().ConfigureAwait(false))
                {
                    logger.LogDebug(statusUpdate);
                }
                
                logger.LogDebug("Initializing PlonkIt Guide done.");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize Qdrant collection: {ex.Message}", ex);
        }
        finally
        {
            // Unlock
            _rebuildStoreLock.Release();
        }
    }

    public async IAsyncEnumerable<string> RebuildStoreAsync()
    {
        // Test the connection
        var connectionExists = await _testConnectionsAsync().ConfigureAwait(false);
        
        // If the connection is not there
        if (connectionExists == false)
        {
            // Return error
            yield return "Could not rebuild PlonkIt Guide vector store because not all connections could be established.";
            yield break;
        }
        
        // Lock
        await _rebuildStoreLock.WaitAsync().ConfigureAwait(false);

        try
        {
            // Check if collection exists
            var collections = await client.ListCollectionsAsync().ConfigureAwait(false);
            var exists = collections.Any(c => c == collectionName);

            // If the collection exists
            if (exists)
            {
                // Delete the collection
                await client.DeleteCollectionAsync(collectionName).ConfigureAwait(false);
            }

            // Create collection
            await client.CreateCollectionAsync(
                collectionName: collectionName,
                vectorsConfig: new VectorParams
                {
                    Size = VectorSize,
                    Distance = Distance.Cosine
                }
            ).ConfigureAwait(false);

            await foreach (var statusUpdate in _initPlonkItStoreAsync().ConfigureAwait(false))
            {
                yield return statusUpdate;
            }
        }
        finally
        {
            // Unlock
            _rebuildStoreLock.Release();
        }

        yield return "Rebuild done.";
    }

    #endregion

    #region Access methods
    
    public async Task<List<SectionRecord>> SearchSectionsAsync(string query, int limit = 5)
    {
        var queryEmbedding = await embeddingService.GenerateAsync(query).ConfigureAwait(false);

        var results = await client.SearchAsync(
            collectionName: collectionName,
            vector: queryEmbedding.Vector.ToArray(),
            limit: (ulong)limit,
            payloadSelector: true
        ).ConfigureAwait(false);

        return results.Select(result => new SectionRecord
            {
                Text = result.Payload["text"].StringValue,
                Source = result.Payload["source"].StringValue,
                Country = result.Payload["country"].StringValue
            })
            .ToList();
    }

    public async Task<List<string>> GetUniqueCountriesAsync()
    {
        var countries = new HashSet<string>();

        // Scroll through all points to get unique countries
        var scrollResponse = await client.ScrollAsync(
            collectionName: collectionName,
            limit: 100,
            payloadSelector: new WithPayloadSelector
            {
                Include = new PayloadIncludeSelector
                {
                    Fields = { "country" }
                }
            }
        ).ConfigureAwait(false);

        foreach (var point in scrollResponse.Result)
        {
            if (!point.Payload.TryGetValue("country", out var value)) continue;
            var country = value.StringValue;
            if (!string.IsNullOrEmpty(country))
            {
                countries.Add(country);
            }
        }

        // Continue scrolling if there are more points
        while (scrollResponse.NextPageOffset != null)
        {
            scrollResponse = await client.ScrollAsync(
                collectionName: collectionName,
                offset: scrollResponse.NextPageOffset,
                limit: 100,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector
                    {
                        Fields = { "country" }
                    }
                }
            ).ConfigureAwait(false);

            foreach (var point in scrollResponse.Result)
            {
                if (!point.Payload.TryGetValue("country", out var value)) continue;
                var country = value.StringValue;
                if (!string.IsNullOrEmpty(country))
                {
                    countries.Add(country);
                }
            }
        }

        return countries.OrderBy(c => c).ToList();
    }

    public async Task<List<SectionRecord>> GetSectionsByCountryAsync(string country)
    {
        // Use filter to get sections by country
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "country",
                        Match = new Match { Keyword = country }
                    }
                }
            }
        };

        var scrollResponse = await client.ScrollAsync(
            collectionName: collectionName,
            filter: filter,
            limit: 100,
            payloadSelector: true
        ).ConfigureAwait(false);

        var sections = scrollResponse.Result.Select(point => new SectionRecord
            {
                Text = point.Payload["text"].StringValue,
                Source = point.Payload["source"].StringValue,
                Country = point.Payload["country"].StringValue
            })
            .ToList();

        // Continue scrolling if there are more points
        while (scrollResponse.NextPageOffset != null)
        {
            scrollResponse = await client.ScrollAsync(
                collectionName: collectionName,
                offset: scrollResponse.NextPageOffset,
                filter: filter,
                limit: 100,
                payloadSelector: true
            ).ConfigureAwait(false);

            sections.AddRange(scrollResponse.Result.Select(point => new SectionRecord
            {
                Text = point.Payload["text"].StringValue,
                Source = point.Payload["source"].StringValue,
                Country = point.Payload["country"].StringValue
            }));
        }

        return sections;
    }

    #endregion
    
    #region Datatypes
    
    private class PlonkItGuide
    {
        public string Title { get; set; } = String.Empty;

        public string Slug { get; set; } = String.Empty;

        public List<string> Cat { get; set; } = [];
    }

    private class PlonkItGuideResponse
    {
        public List<PlonkItGuide> Data { get; set; } = [];
    }

    #endregion
    
    #region Private methods
    
    private async Task<bool> _testConnectionsAsync()
    {
        // Test the llm categorizer connection
        var llmCategorizerConnectionExists = await getPlonkItGuideSectionEmbeddingTextUseCase
            .TestConnectionAsync()
            .ConfigureAwait(false);
        
        // If the connection is not there
        if (llmCategorizerConnectionExists == false)
        {
            return false;
        }
        
        // Test embedding model connection
        var embeddingModelConnectionExists = await embeddingService
            .TestConnectionAsync()
            .ConfigureAwait(false);

        if (embeddingModelConnectionExists == false)
        {
            return false;
        }

        return true;
    }
    
    private async Task _addSectionAsync(string text, string textForEmbedding, string source, string country)
    {
        LogAddingSectionForCountry(logger, country);

        var embedding = await embeddingService
            .GenerateAsync(textForEmbedding)
            .ConfigureAwait(false);

        var payload = new Dictionary<string, Value>
        {
            ["text"] = text,
            ["source"] = source,
            ["country"] = country
        };

        var id = Guid.NewGuid();

        var point = new PointStruct
        {
            Id = new PointId { Uuid = id.ToString() },
            Vectors = embedding.Vector.ToArray(),
            Payload = { payload }
        };

        await client.UpsertAsync(collectionName, [point]).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> _initPlonkItStoreAsync()
    {
        yield return "Reading guides...";

        using var httpClient = new HttpClient();
        
        // Get the guides
        var guides = await httpClient
            .GetFromJsonAsync<PlonkItGuideResponse>("https://www.plonkit.net/api/guides")
            .ConfigureAwait(false);

        // If the guides could not be fetched
        if (guides == null)
        {
            logger.LogError("PlonkItGuide could not be retrieved.");
            yield return "ERROR: PlonkItGuide could not be retrieved.";
            yield break;
        }

        var idx = 0;
        
        // For every guide
        foreach (var guide in guides.Data)
        {
            // Build the string for this guide page
            var guideStatus = $"[{idx++ * 100.0M / guides.Data.Count:F2}%] Fetching '{guide.Title}': ";
            
            // Fetch the plonk it page
            var statusUpdates = 
                _fetchPlonkItCountryPageAsync($"https://www.plonkit.net/{guide.Slug}", guide.Title, guide.Cat);
            
            // For every status update
            await foreach (var statusUpdate in statusUpdates.ConfigureAwait(false))
            {
                // Return status
                yield return guideStatus +  statusUpdate;
            }
        }

        yield return "Initializing PlonkIt Guide done.";
    }

    private async IAsyncEnumerable<string> _fetchPlonkItCountryPageAsync(string url, string guideTitle, ICollection<string> continents)
    {
        yield return "Fetching Page...";
        
        var pageContent = await _fetchPageContentsAsync(url).ConfigureAwait(false);

        if (pageContent == null)
        {
            yield return "ERROR: Page could not be retrieved.";
            yield break;
        }

        // Use HtmlAgilityPack to extract text
        var doc = new HtmlDocument();
        doc.LoadHtml(pageContent);

        var idPattern = IdRegex();

        // Select all divs with an id attribute matching the pattern
        var divs = doc.DocumentNode
            .SelectNodes("//div[@id]")
            .Where(div => idPattern.IsMatch(div.GetAttributeValue("id", "")))
            .ToList();

        if (divs.Count == 0)
        {
            yield return "ERROR: No sections found.";
            yield break;
        }

        var country = url.Split('/').Last();

        var index = 0;
        
        // Create channel to stream out status updates
        var channel = Channel.CreateUnbounded<string>();
        
        var producer = Task.Run(async () =>
        {
            await Parallel.ForEachAsync(divs, new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    config.GetValue<int>(ConfigKeys.EmbeddingMaxDegreeOfParallelismConfigurationKey)
            }, async (div, ct) =>
            {
                try
                {
                    var innerDiv = div.SelectNodes("./div/div");

                    var content = innerDiv?.FirstOrDefault()?.InnerHtml;

                    if (string.IsNullOrEmpty(content))
                    {
                        return;
                    }

                    // Get the id
                    var id = div.Attributes["id"].Value;

                    // Build the source
                    var source = $"{url}#{id}";

                    // Return status
                    await channel.Writer.WriteAsync($"[{index * 100.0M / divs.Count:F2}%] Embedding section #{id}...", ct).ConfigureAwait(false);

                    // Increment the index
                    Interlocked.Increment(ref index);
                    
                    // Get the embedding text
                    var embeddingText = await getPlonkItGuideSectionEmbeddingTextUseCase
                        .GetEmbeddingTextAsync(guideTitle, innerDiv!.First().InnerText, continents)
                        .ConfigureAwait(false);

                    await _addSectionAsync(content, embeddingText, source, country).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Return status
                    await channel.Writer.WriteAsync($"Adding section for plonk-it-guide failed: {e.Message}", ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            
            channel.Writer.Complete();
        });
        
        // Read the channel
        await foreach (var content in channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            yield return content;
        }
        
        await producer.ConfigureAwait(false);
    }

    private async Task<string?> _fetchPageContentsAsync(string url)
    {
        var retryCount = 0;

        tryAgain:
        try
        {
            var browser = await _getBrowserAsync().ConfigureAwait(false);
            var page = await browser.NewPageAsync().ConfigureAwait(false);

            // Navigate and wait for network to be idle
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle2],
                Timeout = 30000
            }).ConfigureAwait(false);

            // Additional wait for dynamic content
            await Task.Delay(2000).ConfigureAwait(false);

            var content = await page.GetContentAsync().ConfigureAwait(false);
            await page.CloseAsync().ConfigureAwait(false);

            return content;
        }
        catch (Exception e)
        {
            LogErrorFetchingPlonkItGuidePageUrl(logger, e, url);
            retryCount++;
            if (retryCount < 5)
            {
                await Task.Delay(5000).ConfigureAwait(false);
                goto tryAgain;
            }
        }

        return null;
    }

    private async Task<IBrowser> _getBrowserAsync()
    {
        if (_browser != null) return _browser;
        // Download Chromium if not already downloaded
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync().ConfigureAwait(false);

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-setuid-sandbox"]
        }).ConfigureAwait(false);
        return _browser;
    }

    #endregion
    
    private IBrowser? _browser;

    [GeneratedRegex(@"^\d+-\d+$")]
    private static partial Regex IdRegex();

    private readonly SemaphoreSlim _rebuildStoreLock = new(1, 1);
    
    [LoggerMessage(LogLevel.Debug, "Adding section for country: {country}.")]
    static partial void LogAddingSectionForCountry(ILogger<PlonkItGuideVectorStore> logger, string country);

    [LoggerMessage(LogLevel.Error, "Error fetching plonk-it-guide page {url}")]
    static partial void LogErrorFetchingPlonkItGuidePageUrl(ILogger<PlonkItGuideVectorStore> logger, Exception ex, string url);
}