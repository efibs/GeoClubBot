using UseCases.InputPorts.SelfRoles;
using UseCases.UseCases.SelfRoles;

namespace GeoClubBot.DependencyInjection.Modules;

public static class SelfRolesModule
{
    public static IServiceCollection AddSelfRolesModule(this IServiceCollection services)
    {
        services.AddTransient<IUpdateSelfRolesMessageUseCase, UpdateSelfRolesMessageUseCase>();

        return services;
    }
}
