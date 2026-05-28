using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record GetLinkedDiscordUserIdQuery(string GeoGuessrUserId) : IQuery<Result<ulong>>;

public sealed record GetLinkedGeoGuessrUserQuery(ulong DiscordUserId) : IQuery<Result<GeoGuessrUser>>;

public sealed record GetOpenAccountLinkingRequestQuery(ulong DiscordUserId) : IQuery<Result<GeoGuessrAccountLinkingRequest>>;

public sealed record GetDiscordUserByNicknameQuery(string Nickname) : IQuery<Result<ulong>>;

public sealed record GetGeoGuessrUserByNicknameQuery(string Nickname) : IQuery<Result<GeoGuessrUser>>;

public sealed record GetGeoGuessrProfileQuery(ulong DiscordUserId) : IQuery<Result<UserDto>>;

public sealed class AccountLinkingQueriesHandler(
    IGeoGuessrUserRepository users,
    IClubMemberRepository members,
    IAccountLinkingRequestRepository requests,
    IGeoGuessrUserProfileReader profileReader)
    : IRequestHandler<GetLinkedDiscordUserIdQuery, Result<ulong>>,
      IRequestHandler<GetLinkedGeoGuessrUserQuery, Result<GeoGuessrUser>>,
      IRequestHandler<GetOpenAccountLinkingRequestQuery, Result<GeoGuessrAccountLinkingRequest>>,
      IRequestHandler<GetDiscordUserByNicknameQuery, Result<ulong>>,
      IRequestHandler<GetGeoGuessrUserByNicknameQuery, Result<GeoGuessrUser>>,
      IRequestHandler<GetGeoGuessrProfileQuery, Result<UserDto>>
{
    private static readonly Error NotLinked =
        Error.NotFound("account_linking.not_linked", "No linked account exists for the requested user.");
    private static readonly Error RequestNotFound =
        Error.NotFound("account_linking.request_not_found", "No account-linking request exists for that Discord user.");
    private static readonly Error MemberNotFound =
        Error.NotFound("club_member.not_found", "No club member exists with that nickname.");
    private static readonly Error ProfileNotFound =
        Error.NotFound("account_linking.profile_not_found", "No GeoGuessr profile could be retrieved for that Discord user.");

    public async Task<Result<ulong>> Handle(GetLinkedDiscordUserIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByUserIdAsync(request.GeoGuessrUserId, cancellationToken).ConfigureAwait(false);
        return user?.DiscordUserId is { } discordUserId
            ? Result<ulong>.Success(discordUserId)
            : NotLinked;
    }

    public async Task<Result<GeoGuessrUser>> Handle(GetLinkedGeoGuessrUserQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        return user is not null ? user : NotLinked;
    }

    public async Task<Result<GeoGuessrAccountLinkingRequest>> Handle(GetOpenAccountLinkingRequestQuery request, CancellationToken cancellationToken)
    {
        var linkingRequest = await requests.ReadRequestAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        return linkingRequest is not null ? linkingRequest : RequestNotFound;
    }

    public async Task<Result<ulong>> Handle(GetDiscordUserByNicknameQuery request, CancellationToken cancellationToken)
    {
        var member = await members.ReadClubMemberByNicknameAsync(request.Nickname, cancellationToken).ConfigureAwait(false);
        return member?.User.DiscordUserId is { } discordUserId
            ? Result<ulong>.Success(discordUserId)
            : NotLinked;
    }

    public async Task<Result<GeoGuessrUser>> Handle(GetGeoGuessrUserByNicknameQuery request, CancellationToken cancellationToken)
    {
        var member = await members.ReadClubMemberByNicknameAsync(request.Nickname, cancellationToken).ConfigureAwait(false);
        return member is not null ? member.User : MemberNotFound;
    }

    public async Task<Result<UserDto>> Handle(GetGeoGuessrProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return NotLinked;
        }

        var profile = await profileReader.ReadUserProfileAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        return profile is not null ? profile : ProfileNotFound;
    }
}
