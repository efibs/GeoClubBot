namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetLinkedDiscordUserIdUseCase
{
    Task<ulong?> GetLinkedDiscordUserIdAsync(string geoGuessrUserId);
}