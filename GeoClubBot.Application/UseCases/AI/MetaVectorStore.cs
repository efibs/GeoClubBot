using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Constants;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;
using PuppeteerSharp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using UseCases.InputPorts.AI;
using Match = Qdrant.Client.Grpc.Match;

namespace UseCases.UseCases.AI;

public class SectionRecord
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public partial class MetaVectorStore(
    QdrantClient client,
    ITextEmbeddingGenerationService embeddingService,
    IGetPlonkItGuideSectionEmbeddingTextUseCase  getPlonkItGuideSectionEmbeddingTextUseCase,
    ILogger<MetaVectorStore> logger,
    IConfiguration config,
    string collectionName = "geoguessr-metas")
{
    private const int VectorSize = 1024;

    public async Task InitializeAsync()
    {
        try
        {
            // Check if collection exists
            var collections = await client.ListCollectionsAsync().ConfigureAwait(false);
            var exists = collections.Any(c => c == collectionName);

            if (!exists)
            {
                // Create collection
                await client.CreateCollectionAsync(
                    collectionName: collectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = VectorSize,
                        Distance = Distance.Cosine
                    }
                ).ConfigureAwait(false);
                
                await _initPlonkItStoreAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize Qdrant collection: {ex.Message}", ex);
        }
    }

    public async Task<Guid> AddSectionAsync(string text, string textForEmbedding, string source, string country, Guid customId)
    {
        logger.LogDebug($"Adding section for country: {country}.");
        
        var hash = ComputeHash(text);

        var embedding = await embeddingService
            .GenerateEmbeddingAsync(textForEmbedding)
            .ConfigureAwait(false);
        
        var payload = new Dictionary<string, Value>
        {
            ["id"] = customId.ToString(),
            ["text"] = text,
            ["source"] = source,
            ["hash"] = hash,
            ["country"] = country
        };

        var point = new PointStruct
        {
            Id = new PointId { Uuid = customId.ToString() },
            Vectors = embedding.ToArray(),
            Payload = { payload }
        };

        await client.UpsertAsync(collectionName, [point]).ConfigureAwait(false);
        return customId;
    }

    public async Task<bool> UpdateSectionIfChangedAsync(Guid id, string text, string textForEmbedding, string source, string country)
    {
        try
        {
            // Try to get existing record
            var points = await client.RetrieveAsync(
                collectionName,
                [new PointId { Uuid = id.ToString() }],
                withPayload: true
            ).ConfigureAwait(false);

            if (points.Count == 0)
            {
                // Record doesn't exist, add it
                await AddSectionAsync(text, textForEmbedding, source, country, id).ConfigureAwait(false);
                return true;
            }

            var existing = points[0];
            var existingHash = existing.Payload["hash"].StringValue;
            var newHash = ComputeHash(text);

            if (existingHash == newHash) return false;
            // Content changed, update it
            await AddSectionAsync(text, textForEmbedding, source, country, id).ConfigureAwait(false);
            return true;

        }
        catch
        {
            // If retrieval fails, treat as new record
            await AddSectionAsync(text, textForEmbedding, source, country, id).ConfigureAwait(false);
            return true;
        }
    }

    public async Task<List<SectionRecord>> SearchSectionsAsync(string query, int limit = 5)
    {
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query).ConfigureAwait(false);
        
        var results = await client.SearchAsync(
            collectionName: collectionName,
            vector: queryEmbedding.ToArray(),
            limit: (ulong)limit,
            payloadSelector: true
        ).ConfigureAwait(false);

        return results.Select(result => new SectionRecord
            {
                Id = result.Payload["id"].StringValue,
                Text = result.Payload["text"].StringValue,
                Source = result.Payload["source"].StringValue,
                Hash = result.Payload["hash"].StringValue,
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
                Id = point.Payload["id"].StringValue,
                Text = point.Payload["text"].StringValue,
                Source = point.Payload["source"].StringValue,
                Hash = point.Payload["hash"].StringValue,
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
                Id = point.Payload["id"].StringValue,
                Text = point.Payload["text"].StringValue,
                Source = point.Payload["source"].StringValue,
                Hash = point.Payload["hash"].StringValue,
                Country = point.Payload["country"].StringValue
            }));
        }

        return sections;
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
    
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
    
    private async Task _initPlonkItStoreAsync()
    {
        logger.LogDebug("Initializing plonk-it-guide");

        try
        {
            using var httpClient = new HttpClient();
            // Get the guides
            var guides = await httpClient
                .GetFromJsonAsync<PlonkItGuideResponse>("https://www.plonkit.net/api/guides")
                .ConfigureAwait(false);

            if (guides == null)
            {
                logger.LogError("PlonkItGuide could not be retrieved.");
                return;
            }
        
            foreach (var guide in guides.Data)
            {
                await _fetchPlonkItCountryPageAsync($"https://www.plonkit.net/{guide.Slug}", guide.Title, guide.Cat).ConfigureAwait(false);
            }
            
            logger.LogDebug("Initializing plonk-it-guide done");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Initializing plonk-it-guide failed");
        }
    }
    
    private async Task _fetchPlonkItCountryPageAsync(string url, string guideTitle, ICollection<string> continents)
    {
        logger.LogDebug($"Fetching plonk-it page {url}");
        
        var pageContent = await _fetchPageContentsAsync(url).ConfigureAwait(false);

        if (pageContent == null)
        {
            logger.LogError("Plonk-it-guide page {url} could not be retrieved.", url);
            return;
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
            return;
        }
        
        var country = url.Split('/').Last();

        await Parallel.ForEachAsync(divs, new ParallelOptions
        {
            MaxDegreeOfParallelism = config.GetValue<int>(ConfigKeys.EmbeddingMaxDegreeOfParallelismConfigurationKey)
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
                var entryId = source.GetHashCode();
                var embeddingText = await getPlonkItGuideSectionEmbeddingTextUseCase
                    .GetEmbeddingTextAsync(guideTitle, innerDiv!.First().InnerText, continents)
                    .ConfigureAwait(false);
                var entryIdGuid = ToGuid(entryId);

                await AddSectionAsync(content, embeddingText, source, country, entryIdGuid).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Adding section for plonk-it-guide failed");
            }
        }).ConfigureAwait(false);
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
            logger.LogError(e, "Error fetching plonk-it-guide page {url}", url);
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
    
    public static Guid ToGuid(int value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
    
    private IBrowser? _browser;

    [GeneratedRegex(@"^\d+-\d+$")]
    private static partial Regex IdRegex();
}