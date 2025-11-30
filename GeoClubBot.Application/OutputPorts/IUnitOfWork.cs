namespace UseCases.OutputPorts;

public interface IUnitOfWork
{
    public IAccountLinkingRequestRepository AccountLinkingRequests { get; }
    
    public IClubChallengeRepository ClubChallenges { get; }
    
    public IClubMemberRepository ClubMembers { get; }
    
    public IClubRepository Clubs { get; }
    
    public IExcusesRepository Excuses { get; }
    
    public IGeoGuessrUserRepository GeoGuessrUsers { get; }
    
    public IHistoryRepository History { get; }
    
    public IStrikesRepository Strikes { get; }

    Task<int> SaveChangesAsync();
}