using Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.RandomDraws;

/// <summary>The countries <c>/random country</c> draws from.</summary>
public sealed record ReadDrawableCountriesQuery(bool StreetViewOnly) : IQuery<IReadOnlyList<DrawableCountry>>;

/// <summary>
/// The current members of one club, or of every club, to draw from.
/// </summary>
/// <param name="ClubId">The club; the main club when null. Ignored with <paramref name="AllClubs"/>.</param>
public sealed record ReadDrawableClubMembersQuery(Guid? ClubId, bool AllClubs) : IQuery<Result<DrawableClubMembers>>;

/// <param name="PoolName">What the members were drawn from: the club's name, or "all our clubs".</param>
public sealed record DrawableClubMembers(string PoolName, IReadOnlyList<DrawableClubMember> Members);

/// <param name="DiscordUserId">Their linked Discord account, if any.</param>
public sealed record DrawableClubMember(string UserId, string Nickname, ulong? DiscordUserId, string ClubName);

public sealed class RandomDrawQueriesHandler(
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IOptions<RandomDrawsConfiguration> randomDrawsConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig)
    : IRequestHandler<ReadDrawableCountriesQuery, IReadOnlyList<DrawableCountry>>,
      IRequestHandler<ReadDrawableClubMembersQuery, Result<DrawableClubMembers>>
{
    public const string AllClubsPoolName = "all our clubs";

    public Task<IReadOnlyList<DrawableCountry>> Handle(ReadDrawableCountriesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(DrawableCountries.For(randomDrawsConfig.Value, request.StreetViewOnly));

    public async Task<Result<DrawableClubMembers>> Handle(ReadDrawableClubMembersQuery request, CancellationToken cancellationToken)
    {
        var allClubs = await clubs.ReadAllClubsAsync(cancellationToken).ConfigureAwait(false);
        var clubNames = allClubs.ToDictionary(club => club.ClubId, club => club.Name);

        if (request.AllClubs)
        {
            var members = await clubMembers.ReadClubMembersAsync(cancellationToken).ConfigureAwait(false);
            return new DrawableClubMembers(AllClubsPoolName, members
                .Where(member => member.ClubId is { } clubId && clubNames.ContainsKey(clubId))
                .Select(member => new DrawableClubMember(
                    member.UserId, member.User.Nickname, member.User.DiscordUserId, clubNames[member.ClubId!.Value]))
                .OrderBy(member => member.Nickname, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }

        var id = request.ClubId ?? geoGuessrConfig.Value.MainClub.ClubId;
        if (!clubNames.TryGetValue(id, out var name))
        {
            return Error.NotFound("random.unknown_club", "Unknown club. Please pick one of the suggested clubs.");
        }

        var clubMembersOfOne = await clubMembers.ReadClubMembersByClubIdAsync(id, cancellationToken).ConfigureAwait(false);
        return new DrawableClubMembers(name, clubMembersOfOne
            .Select(member => new DrawableClubMember(member.UserId, member.User.Nickname, member.User.DiscordUserId, name))
            .OrderBy(member => member.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }
}
