using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using UseCases.UseCases.ClubMembers;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Club;

public sealed class SyncClubsHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _mainClient = Substitute.For<IGeoGuessrClient>();
    private readonly IGeoGuessrClient _secondClient = Substitute.For<IGeoGuessrClient>();
    private readonly ISender _rootSender = Substitute.For<ISender>();
    private readonly ISender _scopedSender = Substitute.For<ISender>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private IReadOnlyList<ClubMemberSyncSnapshot>? _saved;

    public SyncClubsHandlerTests()
    {
        _clientFactory.CreateClient(MainClub).Returns(_mainClient);
        _clientFactory.CreateClient(SecondClub).Returns(_secondClient);
        _mainClient.ReadClubAsync(MainClub, Arg.Any<CancellationToken>()).Returns(GeoGuessrDtos.Club(MainClub, 7, "Main"));
        _secondClient.ReadClubAsync(SecondClub, Arg.Any<CancellationToken>()).Returns(GeoGuessrDtos.Club(SecondClub, 3, "Second"));
        _members.ReadClubMembersByClubIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _rootSender.Send(Arg.Any<SaveClubMembersCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved = [.. call.Arg<SaveClubMembersCommand>().Snapshots];
                return Unit.Value;
            });
    }

    private Task HandleAsync(bool withSecondClub = false)
    {
        var config = new GeoGuessrConfigurationBuilder().WithClub(MainClub);
        if (withSecondClub)
        {
            config.WithClub(SecondClub, isMain: false);
        }

        var scopes = TestScopes.Serving(services => services
            .AddSingleton(_scopedSender)
            .AddSingleton(_clubs)
            .AddSingleton(_members)
            .AddSingleton(_unitOfWork));

        return new SyncClubsHandler(_clientFactory, scopes, _rootSender, config.BuildOptions())
            .Handle(new SyncClubsCommand(), CancellationToken.None);
    }

    private void ApiMembers(IGeoGuessrClient client, Guid clubId, params string[] userIds)
    {
        var club = GeoGuessrDtos.Club(clubId, name: clubId == MainClub ? "Main" : "Second");
        club.Members = [.. userIds.Select(id => GeoGuessrDtos.Member(id, $"nick-{id}", xp: 50))];
        client.ReadClubAsync(clubId, Arg.Any<CancellationToken>()).Returns(club);
    }

    private void StoredMembers(Guid clubId, params string[] userIds) =>
        _members.ReadClubMembersByClubIdAsync(clubId, Arg.Any<CancellationToken>())
            .Returns(userIds.Select(id => new ClubMemberBuilder().WithUserId(id).WithNickname($"stored-{id}").WithXp(10).InClub(clubId).Build()).ToList());

    [Fact]
    public async Task StoresEveryClub_BeforeTheirMembers()
    {
        await HandleAsync(withSecondClub: true);

        await _clubs.Received(1).CreateOrUpdateClubAsync(
            Arg.Is<global::Entities.Club>(c => c.ClubId == MainClub && c.Name == "Main" && c.Level == 7),
            Arg.Any<CancellationToken>());
        await _clubs.Received(1).CreateOrUpdateClubAsync(
            Arg.Is<global::Entities.Club>(c => c.ClubId == SecondClub && c.Level == 3),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheMainClubsLevel_ReachesTheBotStatus()
    {
        await HandleAsync(withSecondClub: true);

        await _scopedSender.Received(1).Send(Arg.Any<SetClubLevelStatusCommand>(), Arg.Any<CancellationToken>());
        await _scopedSender.Received(1).Send(new SetClubLevelStatusCommand(7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CurrentMembers_AreSavedWithTheirClub()
    {
        ApiMembers(_mainClient, MainClub, "a");

        await HandleAsync();

        var snapshot = _saved.Should().ContainSingle().Subject;
        snapshot.UserId.Should().Be("a");
        snapshot.Nickname.Should().Be("nick-a");
        snapshot.TargetClubId.Should().Be(MainClub);
        snapshot.Xp.Should().Be(50);
    }

    [Fact]
    public async Task AMemberWhoLeft_IsSavedWithoutAClub()
    {
        ApiMembers(_mainClient, MainClub);
        StoredMembers(MainClub, "gone");

        await HandleAsync();

        var snapshot = _saved.Should().ContainSingle().Subject;
        snapshot.UserId.Should().Be("gone");
        snapshot.Nickname.Should().Be("stored-gone");
        snapshot.TargetClubId.Should().BeNull();
        snapshot.Xp.Should().Be(10);
    }

    [Fact]
    public async Task AMemberStillThere_IsNotAlsoSavedAsGone()
    {
        ApiMembers(_mainClient, MainClub, "a");
        StoredMembers(MainClub, "a");

        await HandleAsync();

        _saved.Should().ContainSingle().Which.TargetClubId.Should().Be(MainClub);
    }

    [Fact]
    public async Task AMemberWhoSwitchedClubs_IsSavedInTheirNewClubOnly()
    {
        ApiMembers(_mainClient, MainClub);
        ApiMembers(_secondClient, SecondClub, "mover");
        StoredMembers(MainClub, "mover");

        await HandleAsync(withSecondClub: true);

        _saved.Should().ContainSingle().Which.TargetClubId.Should().Be(SecondClub);
    }

    [Fact]
    public async Task MembersOfEveryClub_AreSavedTogether()
    {
        ApiMembers(_mainClient, MainClub, "a");
        ApiMembers(_secondClient, SecondClub, "b");

        await HandleAsync(withSecondClub: true);

        _saved!.Select(s => s.UserId).Should().BeEquivalentTo("a", "b");
        await _rootSender.Received(1).Send(Arg.Any<SaveClubMembersCommand>(), Arg.Any<CancellationToken>());
    }
}
