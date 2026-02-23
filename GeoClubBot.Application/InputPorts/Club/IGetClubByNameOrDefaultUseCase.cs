namespace UseCases.InputPorts.Club;

public interface IGetClubByNameOrDefaultUseCase
{
    Task<Entities.Club?> GetClubByNameOrDefaultAsync(string? clubName);
}