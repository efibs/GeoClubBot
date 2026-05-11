namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetDiscordUserByNicknameUseCase
{
    Task<ulong?> GetDiscordUserIdByNicknameAsync(string nickname);
}
