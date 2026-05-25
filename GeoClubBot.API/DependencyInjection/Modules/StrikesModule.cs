using UseCases.InputPorts.Strikes;
using UseCases.UseCases.Strikes;

namespace GeoClubBot.DependencyInjection.Modules;

public static class StrikesModule
{
    public static IServiceCollection AddStrikesModule(this IServiceCollection services)
    {
        services.AddTransient<ICheckStrikeDecayUseCase, CheckStrikeDecayUseCase>();
        services.AddTransient<IRevokeStrikeUseCase, RevokeStrikeUseCase>();
        services.AddTransient<IUnrevokeStrikeUseCase, UnrevokeStrikeUseCase>();
        services.AddTransient<IAddStrikeUseCase, AddStrikeUseCase>();
        services.AddTransient<IReadAllStrikesUseCase, ReadAllStrikesUseCase>();
        services.AddTransient<IReadAllRelevantStrikesUseCase, ReadAllRelevantStrikesUseCase>();
        services.AddTransient<IReadMemberStrikesUseCase, ReadMemberStrikesUseCase>();

        return services;
    }
}
