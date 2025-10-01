namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface ICancelAccountLinkingUseCase
{
    Task<bool> CancelAccountLinkingAsync(ulong discordUserId, string geoGuessrUserId);
}