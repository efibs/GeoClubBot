namespace UseCases.InputPorts.ClubMemberActivity;

public interface IGetLastCheckTimeUseCase
{
    Task<DateTimeOffset?> GetLastCheckTimeAsync();
}