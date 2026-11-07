using Infrastructure.OutputAdapters.Repositories;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection.Modules;

public static class GeoGuessrIntegrationModule
{
    public static IServiceCollection AddGeoGuessrIntegrationModule(this IServiceCollection services)
    {
        services.AddSingleton<ClubActivityKindClassifier>();

        services.AddTransient<IGeoGuessrActivityReader, CachingGeoGuessrActivityReader>();
        services.AddTransient<IGeoGuessrUserProfileReader, CachingGeoGuessrUserProfileReader>();
        services.AddTransient<IGeoGuessrUserRankedSystemReader, CachingGeoGuessrUserRankedSystemReader>();
        services.AddTransient<IClubMissionBoardReader, CachingClubMissionBoardReader>();
        services.AddTransient<IGeoGuessrClubReader, CachingGeoGuessrClubReader>();

        // Lets a push from GeoGuessr's socket drop the cached board or club it has made stale.
        services.AddSingleton<IGeoGuessrCacheInvalidator, GeoGuessrCacheInvalidator>();

        return services;
    }
}
