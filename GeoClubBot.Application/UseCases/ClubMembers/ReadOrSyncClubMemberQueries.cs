using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;
using Utilities;

namespace UseCases.UseCases.ClubMembers;

public sealed record ReadOrSyncClubMemberByNicknameQuery(string Nickname) : IQuery<Result<ClubMember>>;

public sealed record ReadOrSyncClubMemberByUserIdQuery(string UserId) : IQuery<Result<ClubMember>>;

public sealed class ReadOrSyncClubMemberHandler(
    IClubMemberRepository members,
    IGeoGuessrClientFactory geoGuessrClientFactory,
    ISender mediator,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<ReadOrSyncClubMemberByNicknameQuery, Result<ClubMember>>,
      IRequestHandler<ReadOrSyncClubMemberByUserIdQuery, Result<ClubMember>>
{
    public Task<Result<ClubMember>> Handle(ReadOrSyncClubMemberByNicknameQuery request, CancellationToken cancellationToken) =>
        ReadOrSyncAsync(
            request.Nickname,
            members.ReadClubMemberByNicknameAsync,
            m => m.User.Nickname == request.Nickname,
            $"Club member with nickname '{request.Nickname}' was not found.",
            cancellationToken);

    public Task<Result<ClubMember>> Handle(ReadOrSyncClubMemberByUserIdQuery request, CancellationToken cancellationToken) =>
        ReadOrSyncAsync(
            request.UserId,
            members.ReadClubMemberByUserIdAsync,
            m => m.User.UserId == request.UserId,
            $"Club member with user id '{request.UserId}' was not found.",
            cancellationToken);

    private async Task<Result<ClubMember>> ReadOrSyncAsync<TLookup>(
        TLookup lookupValue,
        Func<TLookup, Task<ClubMember?>> retriever,
        Func<ClubMember, bool> apiMemberPredicate,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var clubMember = await retriever(lookupValue).ConfigureAwait(false);
        if (clubMember is not null)
        {
            return clubMember;
        }

        foreach (var club in geoGuessrConfig.Value.Clubs)
        {
            var client = geoGuessrClientFactory.CreateClient(club.ClubId);
            var apiResponse = await client.ReadClubMembersAsync(club.ClubId).ConfigureAwait(false);
            var apiMembers = ClubMemberAssembler.AssembleEntities(apiResponse, club.ClubId);

            var match = apiMembers.FirstOrDefault(apiMemberPredicate);
            if (match is null)
            {
                continue;
            }

            // Persist this single API member via the standard sync path so the same
            // upsert + domain-event flow runs.
            var snapshot = new ClubMemberSyncSnapshot(
                match.UserId, match.User.Nickname, club.ClubId, match.Xp, match.JoinedAt);
            await mediator
                .Send(new SaveClubMembersCommand([snapshot]), cancellationToken)
                .ConfigureAwait(false);

            return match;
        }

        return Error.NotFound("club_member.not_found", notFoundMessage);
    }
}
