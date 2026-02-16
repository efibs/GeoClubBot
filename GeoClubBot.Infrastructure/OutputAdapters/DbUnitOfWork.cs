using Infrastructure.OutputAdapters.DataAccess;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class DbUnitOfWork : IUnitOfWork
{
    public DbUnitOfWork(GeoClubBotDbContext dbContext)
    {
        _dbContext = dbContext;

        AccountLinkingRequests = new EfAccountLinkingRequestRepository(_dbContext);
        ClubChallenges = new EfClubChallengeRepository(_dbContext);
        ClubMembers = new EfClubMemberRepository(_dbContext);
        Clubs = new EfClubRepository(_dbContext);
        Excuses = new EfExcusesRepository(_dbContext);
        GeoGuessrUsers = new EfGeoGuessrUserRepository(_dbContext);
        History = new EfHistoryRepository(_dbContext);
        Strikes = new EfStrikesRepository(_dbContext);
        DailyMissionReminders = new EfDailyMissionReminderRepository(_dbContext);
    }

    public IAccountLinkingRequestRepository AccountLinkingRequests { get; }

    public IClubChallengeRepository ClubChallenges { get; }

    public IClubMemberRepository ClubMembers { get; }

    public IClubRepository Clubs { get; }

    public IExcusesRepository Excuses { get; }

    public IGeoGuessrUserRepository GeoGuessrUsers { get; }

    public IHistoryRepository History { get; }

    public IStrikesRepository Strikes { get; }

    public IDailyMissionReminderRepository DailyMissionReminders { get; }

    public async Task<int> SaveChangesAsync()
    {
        return await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
    
    private readonly GeoClubBotDbContext _dbContext;
}