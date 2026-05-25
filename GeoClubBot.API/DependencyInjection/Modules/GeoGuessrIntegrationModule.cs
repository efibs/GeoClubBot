using Infrastructure.OutputAdapters;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection.Modules;

public static class GeoGuessrIntegrationModule
{
    public static IServiceCollection AddGeoGuessrIntegrationModule(this IServiceCollection services)
    {
        services.AddTransient<IGeoGuessrActivityReader, CachingGeoGuessrActivityReader>();
        services.AddTransient<IGeoGuessrUserProfileReader, CachingGeoGuessrUserProfileReader>();

        return services;
    }
}
