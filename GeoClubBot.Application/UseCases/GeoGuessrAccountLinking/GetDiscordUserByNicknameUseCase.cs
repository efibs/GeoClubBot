using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetDiscordUserByNicknameUseCase(IUnitOfWork unitOfWork) : IGetDiscordUserByNicknameUseCase
{
    public async Task<ulong?> GetDiscordUserIdByNicknameAsync(string nickname)
    {
        var member = await unitOfWork.ClubMembers.ReadClubMemberByNicknameAsync(nickname).ConfigureAwait(false);
        return member?.User.DiscordUserId;
    }
}
