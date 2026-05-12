using Entities;
using UseCases.InputPorts.GeoGuessrAccountLinking;
using UseCases.OutputPorts;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public class GetGeoGuessrUserByNicknameUseCase(IUnitOfWork unitOfWork) : IGetGeoGuessrUserByNicknameUseCase
{
    public async Task<GeoGuessrUser?> GetGeoGuessrUserByNicknameAsync(string nickname)
    {
        var member = await unitOfWork.ClubMembers.ReadClubMemberByNicknameAsync(nickname).ConfigureAwait(false);
        return member?.User;
    }
}
