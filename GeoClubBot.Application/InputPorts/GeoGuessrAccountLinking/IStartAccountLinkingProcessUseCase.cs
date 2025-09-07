namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IStartAccountLinkingProcessUseCase
{
    Task<string?> StartLinkingProcessAsync(ulong discordUserId, string geoGuessrUserId);
}