using Configuration;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Tools;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Draws the map the bot would attach for a plant, with the bot's own GBIF reader and renderer, and
/// says where the picture was written: the world's part that holds the plant's records, or one country
/// with <c>--country</c>. For looking at a frame or a base-map style before it reaches a channel.
///
/// Reads GBIF only (GETs, no key, no AI request); needs neither the index nor an OpenRouter key.
/// </summary>
public static class MapCommand
{
    public static async Task RunAsync(ProbeArguments arguments, ProbeReport report, CancellationToken cancellationToken)
    {
        if (arguments.Target is not { Length: > 0 } name)
        {
            throw new ProbeException("map needs a plant's name, e.g. map \"khasi pine\" or map cholla --country US");
        }

        string? countryCode = null;
        if (arguments.Country is { } country)
        {
            countryCode = IsoCountries.Resolve(country)
                          ?? throw new ProbeException($"'{country}' is not a country the bot knows by name or code.");
        }

        using var http = new HttpClient(new ReadOnlyHttpHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");

        var clients = new OneClient(http);

        var tools = new AiToolsConfiguration();
        tools.SpeciesRange.Map.TimeoutSeconds = 30;
        var options = Options.Create(tools);

        var reader = new GbifSpeciesRangeReader(clients, options, TimeProvider.System, NullLogger<GbifSpeciesRangeReader>.Instance);
        var read = await reader.ReadAsync(new SpeciesRangeQuery(name, countryCode), cancellationToken).ConfigureAwait(false);
        if (read.IsFailure)
        {
            throw new ProbeException($"GBIF: {read.Error.Message}");
        }

        var range = read.Value;
        var folder = new SpeciesMapFolder(Path.Combine(AppContext.BaseDirectory, "species-maps"));
        var map = new GbifSpeciesRangeMap(clients, folder, options, TimeProvider.System, NullLogger<GbifSpeciesRangeMap>.Instance);
        var link = await map.DrawAsync(range, cancellationToken).ConfigureAwait(false);

        report.Line($"# The map of {range.CanonicalName}");
        report.Line();
        report.Line(SpeciesRangeExcerpt.ToHit(range, name, tools.SpeciesRange.MaxAreas, link).Text);
        report.Line();
        report.Line(link is null ? "_No map could be drawn._" : $"Map: {folder.PathOf(link)}");
    }

    private sealed class OneClient(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
