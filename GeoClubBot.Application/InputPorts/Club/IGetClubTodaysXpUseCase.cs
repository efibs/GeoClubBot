namespace UseCases.InputPorts.Club;

public interface IGetClubTodaysXpUseCase
{
    Task<(int? Xp, string? ClubName)> GetTodaysXpAsync(string? clubName, bool includeWeeklies);
}