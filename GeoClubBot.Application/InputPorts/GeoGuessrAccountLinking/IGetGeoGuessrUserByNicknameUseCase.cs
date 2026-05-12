using Entities;

namespace UseCases.InputPorts.GeoGuessrAccountLinking;

public interface IGetGeoGuessrUserByNicknameUseCase
{
    Task<GeoGuessrUser?> GetGeoGuessrUserByNicknameAsync(string nickname);
}
