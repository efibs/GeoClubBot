namespace UseCases.InputPorts.Club;

public interface ISyncClubUseCase
{
    Task SyncClubAsync(Guid clubId);
}