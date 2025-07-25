using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfExcusesRepository(GeoClubBotDbContext dbContext) : IExcusesRepository
{
    public Task<ClubMemberExcuse?> CreateExcuseAsync(ClubMemberExcuse excuse)
    {
        throw new NotImplementedException();
    }

    public Task<List<ClubMemberExcuse>> ReadExcusesAsync()
    {
        throw new NotImplementedException();
    }

    public Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteExcuseByIdAsync(Guid excuseId)
    {
        throw new NotImplementedException();
    }

    public Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold)
    {
        throw new NotImplementedException();
    }
}