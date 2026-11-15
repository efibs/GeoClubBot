using Configuration;
using Infrastructure.OutputAdapters.AI.Regions;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Services;

/// <summary>
/// Starts reading the Natural Earth boundaries when the host starts, so the first answer that names a
/// region is not the one that waits for a 40 MB download — and the towns screenshots are placed by,
/// which read the boundaries too. Does nothing when the AI, or both features, are off; the reads run on
/// background tasks and never hold start-up.
/// </summary>
public sealed class RegionBoundariesWarmup(
    NaturalEarthRegionBoundaries boundaries,
    GeoNamesPlaceDirectory places,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiToolsConfiguration> toolsConfiguration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!aiConfiguration.Value.Active)
        {
            return Task.CompletedTask;
        }

        var tools = toolsConfiguration.Value;
        if (tools.RegionMap.Enabled)
        {
            boundaries.Warm();
        }

        var plants = tools.PlantIdentification;
        if (plants is { Enabled: true, Location.Enabled: true } && !string.IsNullOrWhiteSpace(plants.ApiKey))
        {
            places.Warm();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
