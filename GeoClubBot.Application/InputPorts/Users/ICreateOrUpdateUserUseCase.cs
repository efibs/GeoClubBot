using Entities;

namespace UseCases.InputPorts.Users;

public interface ICreateOrUpdateUserUseCase
{
    Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user);
}