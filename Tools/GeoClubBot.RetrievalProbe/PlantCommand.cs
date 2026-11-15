using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Configuration;
using Infrastructure.OutputAdapters.AI.Gbif;
using Infrastructure.OutputAdapters.AI.PlantNet;
using Infrastructure.OutputAdapters.AI.Regions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Identifies the plant in one or more screenshots exactly as the bot would before answering — Pl@ntNet,
/// where the question says the screenshot was taken, GBIF's records there — and shows every step: the
/// place read from the question, Pl@ntNet's candidates in its own order and in the weighed one with their
/// records, and the excerpt and notes the model and the member would get. For telling whether a plant was
/// misnamed by the picture, the place or the weighing, and for trying other weights with <c>--set</c>.
///
/// Pictures are files (or links); a file is served to the bot's adapter as if it were a link. Pl@ntNet's
/// answers are kept on disk next to the probe by the pictures and the request, so a rerun with other
/// weights costs no identification. Needs a Pl@ntNet key (<see cref="ProbeSettings.ResolvePlantNetKey"/>);
/// GBIF, GeoNames and Natural Earth are read with GETs only. Needs neither the index nor OpenRouter.
/// </summary>
public static class PlantCommand
{
    public static async Task RunAsync(ProbeArguments arguments, ProbeReport report, CancellationToken cancellationToken)
    {
        if (arguments.Target is not { Length: > 0 } target)
        {
            throw new ProbeException("plant needs a screenshot, e.g. plant shot.png --question \"what tree is this? south of Boston\"");
        }

        var (key, keySource) = ProbeSettings.ResolvePlantNetKey();
        if (key is null)
        {
            throw new ProbeException(
                "No Pl@ntNet key found. Set PLANTNET_API_KEY, or put { \"PlantNet\": { \"ApiKey\": \"...\" } } into "
                + "Tools/GeoClubBot.RetrievalProbe/appsettings.Local.json (git-ignored). Each new identification uses one of the key's 500 a day.");
        }

        var root = ProbeSettings.RepositoryRoot() ?? throw new ProbeException("Run the probe from inside the repository.");
        var settings = BotSettings.Load(BotSettings.DefaultPath(root), arguments.Overrides ?? [], apiKey: null);
        var tools = settings.Tools;
        tools.PlantIdentification.ApiKey = key;
        tools.PlantIdentification.Enabled = true;
        tools.RegionMap.DataDirectory = Path.Combine(AppContext.BaseDirectory, "ai-data");
        tools.SpeciesRange.Map.TimeoutSeconds = 30;
        var options = Options.Create(tools);
        var plants = tools.PlantIdentification;

        var pictures = target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(picture => File.Exists(picture) ? new Uri(Path.GetFullPath(picture)).AbsoluteUri : picture)
            .ToList();
        var question = arguments.Question ?? "What plant is this?";

        using var logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information).AddProvider(new StandardErrorLogger()));
        using var reads = new HttpClient(new ReadOnlyHttpHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromMinutes(5) };
        reads.DefaultRequestHeaders.UserAgent.ParseAdd("GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");

        var cacheDirectory = Path.Combine(AppContext.BaseDirectory, "plantnet-cache");
        using var plantNet = new HttpClient(new CachingPlantNetHandler(cacheDirectory, plants.BaseUrl, new HttpClientHandler()))
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(30, plants.RequestTimeoutSeconds))
        };

        var clients = new Clients(new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            [PlantNetPlantIdentifier.HttpClientName] = plantNet,
            [GbifSpeciesRangeReader.HttpClientName] = reads,
            [NaturalEarthRegionBoundaries.HttpClientName] = reads,
            [GeoNamesPlaceDirectory.HttpClientName] = reads
        });

        var clock = TimeProvider.System;
        var boundaries = new NaturalEarthRegionBoundaries(clients, options, logging.CreateLogger<NaturalEarthRegionBoundaries>());
        var directory = new GeoNamesPlaceDirectory(clients, boundaries, options, logging.CreateLogger<GeoNamesPlaceDirectory>());
        var places = new KnownPlaceDirectory(directory);
        var identifier = new PlantNetPlantIdentifier(clients, options, clock, logging.CreateLogger<PlantNetPlantIdentifier>());
        var records = new GbifPlantRecordCounter(clients, options, clock, logging.CreateLogger<GbifPlantRecordCounter>());
        var ranges = new GbifSpeciesRangeReader(clients, options, clock, logging.CreateLogger<GbifSpeciesRangeReader>());
        var maps = new GbifSpeciesRangeMap(
            clients, new SpeciesMapFolder(Path.Combine(AppContext.BaseDirectory, "species-maps")), options, clock, logging.CreateLogger<GbifSpeciesRangeMap>());
        var lookAhead = new PlantIdentificationLookAhead(identifier, records, ranges, maps, places, options, clock, logging.CreateLogger<PlantIdentificationLookAhead>());

        // Read once up front, as the bot does at start-up: the look-ahead gives the places three seconds.
        Console.Error.WriteLine("Reading the towns and regions (downloaded once next to the probe)…");
        var index = await places.ReadAsync(cancellationToken).ConfigureAwait(false);

        var earlier = arguments.Source is { Length: > 0 } said ? new[] { said } : [];
        var place = plants.Location.Enabled ? ScreenshotPlaceReader.Find(question, index, IsoCountries.Gazetteer, earlier) : null;

        report.Line("# What the bot makes of the screenshot's plant");
        report.Line();
        report.Line($"- Pictures: {string.Join(", ", pictures.Select(Path.GetFileName))}");
        report.Line($"- Question: \"{question}\"" + (earlier.Length > 0 ? $" (earlier in the branch: \"{earlier[0]}\")" : string.Empty));
        report.Line($"- Place: {(place is null ? "none said" : $"{place.Name} — {place.Precision}, from \"{place.Said}\"; records counted {place.DescribeArea(plants.Location.RadiusKm)}")}");
        report.Line(string.Create(CultureInfo.InvariantCulture,
            $"- Settings: {plants.CandidatesConsidered} candidates, RecordsWeight {plants.Location.RecordsWeight}, UnrecordedFactor {plants.Location.UnrecordedFactor}, MinConfidence {plants.MinConfidence}, MinImageScore {plants.MinImageScore}"));
        report.Line($"- Pl@ntNet key from {keySource}");
        report.Line();

        // The same identification the look-ahead makes, from the adapter's cache once asked.
        var identified = await identifier.IdentifyAsync(pictures, cancellationToken).ConfigureAwait(false);
        if (identified.IsFailure)
        {
            report.Line($"Pl@ntNet: {identified.Error.Code} — {identified.Error.Message}");
        }
        else
        {
            var identification = identified.Value;
            report.Line($"Pl@ntNet's genera: {string.Join(", ", identification.Genera.Take(5).Select(genus => $"{genus.Name} {Percent(genus.Score)}"))}");
            report.Line();

            IReadOnlyDictionary<long, long>? local = null, country = null;
            if (place is not null && place.LocalArea(plants.Location.RadiusKm) is { } area)
            {
                var keys = identification.Candidates.Select(candidate => candidate.GbifTaxonKey).OfType<long>().ToList();
                var localRead = await records.CountAsync(keys, area, cancellationToken).ConfigureAwait(false);
                local = localRead.IsSuccess ? localRead.Value : null;
                if (place.CountryArea is { } countryArea)
                {
                    var countryRead = await records.CountAsync(keys, countryArea, cancellationToken).ConfigureAwait(false);
                    country = countryRead.IsSuccess ? countryRead.Value : null;
                }
            }

            var weighing = PlantLocationWeighting.Weigh(identification, place, local, country, plants.Location.RecordsWeight, plants.Location.UnrecordedFactor);
            report.Line("| Weighed | Picture | Share | Image score | Near | Country | Species |");
            report.Line("|---:|---:|---:|---:|---:|---:|---|");
            foreach (var (candidate, rank) in weighing.Candidates.Take(12).Select((candidate, rank) => (candidate, rank)))
            {
                var pictureRank = identification.Candidates.ToList().IndexOf(candidate.Candidate) + 1;
                report.Line($"| {rank + 1} | {pictureRank} | {Percent(candidate.Share)} | {Percent(candidate.Candidate.Score)} | "
                            + $"{candidate.LocalRecords?.ToString("N0", CultureInfo.InvariantCulture) ?? "–"} | {candidate.CountryRecords?.ToString("N0", CultureInfo.InvariantCulture) ?? "–"} | "
                            + $"{candidate.Candidate.ScientificName} ({string.Join(", ", candidate.Candidate.CommonNames.Take(2))}) |");
            }

            report.Line();
            report.Line($"Identified: {weighing.IsIdentified(plants.MinConfidence, plants.MinImageScore)}");
            report.Line();
        }

        var offer = await lookAhead.ReadAsync(question, pictures, earlier, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            report.Line("_The look-ahead offered nothing: the question does not read as asking what plant this is._");
            return;
        }

        foreach (var hit in offer.Hits)
        {
            report.Line($"## Excerpt: {hit.SectionPath ?? hit.Title}");
            report.Line();
            report.Line(hit.Text);
            report.Line();
            if (hit.ImageUrl is { } map)
            {
                report.Line($"Map: {map}");
                report.Line();
            }
        }

        foreach (var notice in offer.Notices)
        {
            report.Line($"> ⚠️ {notice}");
        }
    }

    private static string Percent(double score) => score.ToString("P1", CultureInfo.InvariantCulture);

    /// <summary>The bot's own log lines on standard error, so the report on standard output stays clean.</summary>
    private sealed class StandardErrorLogger : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Console.Error.WriteLine($"  [{logLevel}] {formatter(state, exception)}{(exception is null ? string.Empty : $" ({exception.GetType().Name}: {exception.Message})")}");

        public void Dispose()
        {
        }
    }

    private sealed class Clients(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            clients.TryGetValue(name, out var client)
                ? client
                : throw new InvalidOperationException($"GeoClubBot.RetrievalProbe has no '{name}' client: refusing to make one.");
    }

    /// <summary>
    /// Pl@ntNet's client in the probe: serves <c>file://</c> pictures from disk, lets GETs of links
    /// through, and sends only identifications — each kept on disk by its request (the key left out), so
    /// asking the same pictures again costs nothing.
    /// </summary>
    private sealed class CachingPlantNetHandler(string directory, string baseUrl, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (request.Method == HttpMethod.Get && uri.IsFile)
            {
                var bytes = await File.ReadAllBytesAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                    Path.GetExtension(uri.LocalPath).ToLowerInvariant() switch
                    {
                        ".png" => "image/png",
                        ".webp" => "image/webp",
                        ".gif" => "image/gif",
                        _ => "image/jpeg"
                    });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
            }

            if (request.Method == HttpMethod.Get)
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (request.Method != HttpMethod.Post || !uri.AbsoluteUri.StartsWith($"{baseUrl.TrimEnd('/')}/v2/identify/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"GeoClubBot.RetrievalProbe only identifies with Pl@ntNet: refusing {request.Method} {uri}.");
            }

            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var query = string.Join('&', uri.Query.TrimStart('?').Split('&').Where(part => !part.StartsWith("api-key=", StringComparison.Ordinal)));
            var hash = Convert.ToHexString(SHA256.HashData([.. Encoding.UTF8.GetBytes($"{uri.AbsolutePath}?{query}\n"), .. StripBoundaries(body)]))[..32];
            var path = Path.Combine(directory, $"{hash}.json");

            if (File.Exists(path))
            {
                var cached = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var status = (HttpStatusCode)int.Parse(cached[..3], CultureInfo.InvariantCulture);
                return new HttpResponseMessage(status) { Content = new StringContent(cached[4..], Encoding.UTF8, "application/json"), RequestMessage = request };
            }

            // The multipart content was read to hash it; send a copy.
            var forwarded = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
            foreach (var header in request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            {
                forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await base.SendAsync(forwarded, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 200 or 404)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(path, $"{(int)response.StatusCode:000} {text}", cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(response.StatusCode) { Content = new StringContent(text, Encoding.UTF8, "application/json"), RequestMessage = request };
        }

        /// <summary>The body without its multipart boundaries, which are random per request and would defeat the cache.</summary>
        private static byte[] StripBoundaries(byte[] body)
        {
            var text = Encoding.Latin1.GetString(body);
            var boundary = text.Split("\r\n", 2)[0];
            return boundary.StartsWith("--", StringComparison.Ordinal) ? Encoding.Latin1.GetBytes(text.Replace(boundary, "--", StringComparison.Ordinal)) : body;
        }
    }
}
