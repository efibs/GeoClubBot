using Entities;
using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.GeoGuessrAccountLinking;

public sealed record GetLinkedDiscordUserIdQuery(string GeoGuessrUserId) : IQuery<ulong?>;

public sealed record GetLinkedGeoGuessrUserQuery(ulong DiscordUserId) : IQuery<GeoGuessrUser?>;

public sealed record GetOpenAccountLinkingRequestQuery(ulong DiscordUserId) : IQuery<GeoGuessrAccountLinkingRequest?>;

public sealed record GetDiscordUserByNicknameQuery(string Nickname) : IQuery<ulong?>;

public sealed record GetGeoGuessrUserByNicknameQuery(string Nickname) : IQuery<GeoGuessrUser?>;

public sealed record GetGeoGuessrProfileQuery(ulong DiscordUserId) : IQuery<UserDto?>;

public sealed class AccountLinkingQueriesHandler(
    IGeoGuessrUserRepository users,
    IClubMemberRepository members,
    IAccountLinkingRequestRepository requests,
    IGeoGuessrUserProfileReader profileReader)
    : IRequestHandler<GetLinkedDiscordUserIdQuery, ulong?>,
      IRequestHandler<GetLinkedGeoGuessrUserQuery, GeoGuessrUser?>,
      IRequestHandler<GetOpenAccountLinkingRequestQuery, GeoGuessrAccountLinkingRequest?>,
      IRequestHandler<GetDiscordUserByNicknameQuery, ulong?>,
      IRequestHandler<GetGeoGuessrUserByNicknameQuery, GeoGuessrUser?>,
      IRequestHandler<GetGeoGuessrProfileQuery, UserDto?>
{
    public async Task<ulong?> Handle(GetLinkedDiscordUserIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByUserIdAsync(request.GeoGuessrUserId).ConfigureAwait(false);
        return user?.DiscordUserId;
    }

    public Task<GeoGuessrUser?> Handle(GetLinkedGeoGuessrUserQuery request, CancellationToken cancellationToken) =>
        users.ReadUserByDiscordUserIdAsync(request.DiscordUserId);

    public Task<GeoGuessrAccountLinkingRequest?> Handle(GetOpenAccountLinkingRequestQuery request, CancellationToken cancellationToken) =>
        requests.ReadRequestAsync(request.DiscordUserId);

    public async Task<ulong?> Handle(GetDiscordUserByNicknameQuery request, CancellationToken cancellationToken)
    {
        var member = await members.ReadClubMemberByNicknameAsync(request.Nickname).ConfigureAwait(false);
        return member?.User.DiscordUserId;
    }

    public async Task<GeoGuessrUser?> Handle(GetGeoGuessrUserByNicknameQuery request, CancellationToken cancellationToken)
    {
        var member = await members.ReadClubMemberByNicknameAsync(request.Nickname).ConfigureAwait(false);
        return member?.User;
    }

    public async Task<UserDto?> Handle(GetGeoGuessrProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        return await profileReader.ReadUserProfileAsync(user.UserId).ConfigureAwait(false);
    }
}
