using Configuration;
using Infrastructure.OutputAdapters.AI.Regions;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Services;

/// <summary>
/// Starts reading the Natural Earth boundaries when the host starts, so the first answer that names a
/// region is not the one that waits for a 40 MB download. Does nothing when the AI, or region maps,
/// are off; the read itself runs on a background task and never holds start-up.
/// </summary>
public sealed class RegionBoundariesWarmup(
    NaturalEarthRegionBoundaries boundaries,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiToolsConfiguration> toolsConfiguration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (aiConfiguration.Value.Active && toolsConfiguration.Value.RegionMap.Enabled)
        {
            boundaries.Warm();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
