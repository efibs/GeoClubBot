namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IUnlinkAccountsUseCase
{
    Task<bool> UnlinkAccountsAsync(ulong discordUserId, string geoGuessrUserId);
}