using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Rendering;

namespace UseCases.UseCases.ClubMemberActivity;

public sealed record RenderPlayerActivityQuery(
    string Nickname,
    int MaxNumHistoryEntries,
    string? ClubName) : IQuery<MemoryStream?>;

public sealed class RenderPlayerActivityHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IHistoryRepository history,
    IHistoryRenderer historyRenderer,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig)
    : IRequestHandler<RenderPlayerActivityQuery, MemoryStream?>
{
    public async Task<MemoryStream?> Handle(RenderPlayerActivityQuery request, CancellationToken cancellationToken)
    {
        var clubId = await ResolveClubId(request.ClubName, request.Nickname, cancellationToken).ConfigureAwait(false);
        if (clubId is null)
        {
            return null;
        }

        var playersHistoryEntries = await history
            .ReadHistoryEntriesByPlayerNicknameAsync(request.Nickname, clubId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (playersHistoryEntries is null)
        {
            return null;
        }

        var member = await clubMembers
            .ReadClubMemberByNicknameAsync(request.Nickname, cancellationToken)
            .ConfigureAwait(false);

        if (member is not null)
        {
            playersHistoryEntries = playersHistoryEntries
                .Prepend(ClubMemberHistoryEntry.Create(member.UserId, member.ClubId!.Value, 0, member.JoinedAt))
                .ToList();
        }

        if (playersHistoryEntries.Count < 2)
        {
            return null;
        }

        var weeklyXpTarget = ResolveMinXP(member);

        var entriesOrdered = playersHistoryEntries.OrderBy(e => e.Timestamp);

        var entriesToShow = entriesOrdered
            .Skip(playersHistoryEntries.Count - request.MaxNumHistoryEntries - 1)
            .ToList();

        var values = entriesToShow
            .Skip(1)
            .Zip(entriesToShow, (a, b) => a.Xp - b.Xp)
            .ToList();

        var timestamps = entriesToShow.Select(e => e.Timestamp).ToList();

        return historyRenderer.RenderHistory(values, timestamps, weeklyXpTarget);
    }

    private int ResolveMinXP(ClubMember? member)
    {
        if (member is null)
        {
            return activityCheckerConfig.Value.MinXP;
        }

        var clubEntry = geoGuessrConfig.Value.Clubs.FirstOrDefault(c => c.ClubId == member.ClubId);
        return clubEntry?.GetMinXP(activityCheckerConfig.Value) ?? activityCheckerConfig.Value.MinXP;
    }

    private async Task<Guid?> ResolveClubId(string? clubName, string memberNickname, CancellationToken cancellationToken)
    {
        if (clubName is null)
        {
            var clubMember = await clubMembers
                .ReadClubMemberByNicknameAsync(memberNickname, cancellationToken)
                .ConfigureAwait(false);
            return clubMember?.ClubId;
        }

        var club = await clubs.ReadClubByNameAsync(clubName, cancellationToken).ConfigureAwait(false);
        return club?.ClubId;
    }
}
