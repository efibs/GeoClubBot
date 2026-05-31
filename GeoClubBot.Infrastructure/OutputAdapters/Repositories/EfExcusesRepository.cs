using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Projections;
using Utilities;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfExcusesRepository(GeoClubBotDbContext dbContext) : IExcusesRepository
{
    public ClubMemberExcuse CreateExcuse(ClubMemberExcuse excuse)
    {
        dbContext.Add(excuse);
        return excuse;
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .Include(e => e.ClubMember)
            .ThenInclude(m => m!.User)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ExcuseProjection>> ReadExcuseProjectionsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .Select(e => new ExcuseProjection(e.UserId, e.From, e.To))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberExcuse?> ReadExcuseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ExcuseId == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClubMemberExcuse?> ReadForUpdateByIdAsync(Guid excuseId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberExcuses
            .FirstOrDefaultAsync(e => e.ExcuseId == excuseId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberExcuse>> ReadExcusesByMemberNicknameAsync(string memberNickname, CancellationToken cancellationToken = default)
    {
        var member = await dbContext.ClubMembers
            .AsNoTracking()
            .Include(clubMember => clubMember.Excuses)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.User!.Nickname == memberNickname, cancellationToken)
            .ConfigureAwait(false);

        return member?.Excuses ?? [];
    }

    public void DeleteExcuse(ClubMemberExcuse excuse)
    {
        dbContext.ClubMemberExcuses.Remove(excuse);
    }

    public async Task<int> DeleteExcusesBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        return await dbContext.ClubMemberExcuses
            .Where(e => e.To < threshold)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<ClubMemberRelevantExcuse>> ReadAllRelevantExcusesAsync(int upcomingExcusesNumDays,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var upcomingThreshold = now.AddDays(upcomingExcusesNumDays);

        // Project to an anonymous type so EF Core can translate the navigation JOIN to SQL.
        // TimeRange construction happens client-side after the data is fetched.
        var rows = await dbContext.ClubMemberExcuses
            .AsNoTracking()
            .Where(e => e.To >= now && e.From <= upcomingThreshold)
            .Select(e => new
            {
                Nickname = e.ClubMember!.User.Nickname,
                e.From,
                e.To,
                IsUpcoming = e.From > now
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new ClubMemberRelevantExcuse(r.Nickname, new TimeRange(r.From, r.To), r.IsUpcoming))
            .ToList();
    }
}
