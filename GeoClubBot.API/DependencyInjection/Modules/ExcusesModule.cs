using UseCases.InputPorts.Excuses;
using UseCases.UseCases.Excuses;

namespace GeoClubBot.DependencyInjection.Modules;

public static class ExcusesModule
{
    public static IServiceCollection AddExcusesModule(this IServiceCollection services)
    {
        services.AddTransient<IAddExcuseUseCase, AddExcuseUseCase>();
        services.AddTransient<IUpdateExcuseUseCase, UpdateExcuseUseCase>();
        services.AddTransient<IRemoveExcuseUseCase, RemoveExcuseUseCase>();
        services.AddTransient<IReadExcusesUseCase, ReadExcusesUseCase>();

        return services;
    }
}
