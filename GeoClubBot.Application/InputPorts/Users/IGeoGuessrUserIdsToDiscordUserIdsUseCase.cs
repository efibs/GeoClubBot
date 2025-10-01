namespace UseCases.InputPorts.Users;

public interface IGeoGuessrUserIdsToDiscordUserIdsUseCase
{
    Task<List<ulong>> GetDiscordUserIdsAsync(IEnumerable<string> geoGuessrUserIds);
}