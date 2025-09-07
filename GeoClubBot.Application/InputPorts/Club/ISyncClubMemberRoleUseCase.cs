namespace UseCases.InputPorts.Club;

public interface ISyncClubMemberRoleUseCase
{
    Task SyncAllUsersClubMemberRoleAsync();
    
    Task SyncUserClubMemberRoleAsync(ulong discordUserId, string geoGuessrUserId);
}