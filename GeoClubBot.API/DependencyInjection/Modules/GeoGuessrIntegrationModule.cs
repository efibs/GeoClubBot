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

        return services;
    }
}
