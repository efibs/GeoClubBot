using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetGeoGuessrProfileUseCase(
    IUnitOfWork unitOfWork,
    IGeoGuessrUserProfileReader profileReader) : IGetGeoGuessrProfileUseCase
{
    public async Task<UserDto?> GetGeoGuessrProfileAsync(ulong discordUserId)
    {
        var user = await unitOfWork.GeoGuessrUsers
            .ReadUserByDiscordUserIdAsync(discordUserId).ConfigureAwait(false);

        if (user is null)
            return null;

        return await profileReader.ReadUserProfileAsync(user.UserId).ConfigureAwait(false);
    }
}
