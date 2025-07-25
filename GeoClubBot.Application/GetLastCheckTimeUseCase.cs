using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class GetLastCheckTimeUseCase(IReadOrSyncClubUseCase readOrSyncClubUseCase, IClubRepository historyRepository) : IGetLastCheckTimeUseCase
{
    public async Task<DateTimeOffset?> GetLastCheckTimeAsync()
    {
        // Get the club
        var club = await readOrSyncClubUseCase.ReadOrSyncClubAsync();

        // Get the latest activity check time of the club
        return club.LatestActivityCheckTime;
    }
}