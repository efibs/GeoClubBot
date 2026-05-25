using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.UseCases.GeoGuessrAccountLinking;

namespace GeoClubBot.DependencyInjection.Modules;

public static class AccountLinkingModule
{
    public static IServiceCollection AddAccountLinkingModule(this IServiceCollection services)
    {
        services.AddTransient<IStartAccountLinkingProcessUseCase, StartAccountLinkingUseCase>();
        services.AddTransient<ICompleteAccountLinkingUseCase, CompleteAccountLinkingUseCase>();
        services.AddTransient<IUnlinkAccountsUseCase, UnlinkAccountsUseCase>();
        services.AddTransient<ICancelAccountLinkingUseCase, CancelAccountLinkingUseCase>();
        services.AddTransient<IGetLinkedDiscordUserIdUseCase, GetLinkedDiscordUserIdUseCase>();
        services.AddTransient<IGetLinkedGeoGuessrUserUseCase, GetLinkedGeoGuessrUserUseCase>();
        services.AddTransient<IGetDiscordUserByNicknameUseCase, GetDiscordUserByNicknameUseCase>();
        services.AddTransient<IGetGeoGuessrUserByNicknameUseCase, GetGeoGuessrUserByNicknameUseCase>();
        services.AddTransient<IGetGeoGuessrProfileUseCase, GetGeoGuessrProfileUseCase>();
        services.AddTransient<IGetOpenAccountLinkingRequestUseCase, GetOpenAccountLinkingRequestUseCase>();

        return services;
    }
}
