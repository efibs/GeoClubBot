using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Club;

public sealed class CheckClubLevelHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SecondClub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _mainClient = Substitute.For<IGeoGuessrClient>();
    private readonly IGeoGuessrClient _secondClient = Substitute.For<IGeoGuessrClient>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClubEventNotifier _notifier = Substitute.For<IClubEventNotifier>();
    private readonly ClubLevelTracker _tracker = new();

    public CheckClubLevelHandlerTests()
    {
        _clientFactory.CreateClient(MainClub).Returns(_mainClient);
        _clientFactory.CreateClient(SecondClub).Returns(_secondClient);

        // Nothing to seed from the database: tests set what the tracker knows themselves.
        _clubs.ReadClubByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);
    }

    private CheckClubLevelHandler CreateHandler(params Guid[] secondClubs)
    {
        var config = new GeoGuessrConfigurationBuilder().WithClub(MainClub);
        foreach (var club in secondClubs)
        {
            config.WithClub(club, isMain: false);
        }

        var scopes = TestScopes.Serving(services => services
            .AddSingleton(_sender)
            .AddSingleton(_clubs)
            .AddSingleton(_unitOfWork)
            .AddSingleton(_notifier));

        return new CheckClubLevelHandler(
            _clientFactory, _clubs, _tracker, scopes, config.BuildOptions(), NullLogger<CheckClubLevelHandler>.Instance);
    }

    private void ClubReportsLevel(IGeoGuessrClient client, Guid clubId, int level) =>
        client.ReadClubAsync(clubId, Arg.Any<CancellationToken>()).Returns(GeoGuessrDtos.Club(clubId, level));

    private global::Entities.Club StoredClub(Guid clubId, int level)
    {
        var club = global::Entities.Club.Create(clubId, "Stored", level);
        _clubs.ReadForUpdateByIdAsync(clubId, Arg.Any<CancellationToken>()).Returns(club);
        return club;
    }

    [Fact]
    public async Task UnchangedLevel_DoesNothing()
    {
        _tracker.Set(MainClub, 5);
        ClubReportsLevel(_mainClient, MainClub, 5);

        await CreateHandler().Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().Send(default(SetClubLevelStatusCommand)!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().SendClubLevelUpEvent(default!, default);
        _tracker.TryGet(MainClub).Should().Be(5);
    }

    [Fact]
    public async Task MainClubLevelUp_UpdatesTheStatusAndStoredClub_ThenAnnouncesIt()
    {
        _tracker.Set(MainClub, 5);
        ClubReportsLevel(_mainClient, MainClub, 6);
        var stored = StoredClub(MainClub, 5);

        await CreateHandler().Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _sender.Received(1).Send(new SetClubLevelStatusCommand(6), Arg.Any<CancellationToken>());
        stored.Level.Should().Be(6);
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _notifier.SendClubLevelUpEvent(stored, Arg.Any<CancellationToken>());
        });
        _tracker.TryGet(MainClub).Should().Be(6);
    }

    [Fact]
    public async Task SecondClubLevelUp_IsAnnounced_ButNeverTouchesTheBotStatus()
    {
        _tracker.Set(MainClub, 5);
        _tracker.Set(SecondClub, 2);
        ClubReportsLevel(_mainClient, MainClub, 5);
        ClubReportsLevel(_secondClient, SecondClub, 3);
        var stored = StoredClub(SecondClub, 2);

        await CreateHandler(SecondClub).Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _sender.DidNotReceiveWithAnyArgs().Send(default(SetClubLevelStatusCommand)!, default);
        await _notifier.Received(1).SendClubLevelUpEvent(stored, Arg.Any<CancellationToken>());
        _tracker.TryGet(SecondClub).Should().Be(3);
    }

    [Fact]
    public async Task FirstSightingOfAClub_SeedsTheTrackerAndStatus_WithoutALevelUpAnnouncement()
    {
        ClubReportsLevel(_mainClient, MainClub, 9);

        await CreateHandler().Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _sender.Received(1).Send(new SetClubLevelStatusCommand(9), Arg.Any<CancellationToken>());
        await _clubs.DidNotReceiveWithAnyArgs().ReadForUpdateByIdAsync(default, default);
        await _notifier.DidNotReceiveWithAnyArgs().SendClubLevelUpEvent(default!, default);
        _tracker.TryGet(MainClub).Should().Be(9);
    }

    [Fact]
    public async Task ClubMissingFromTheDatabase_StillAdvancesTheTracker_WithoutAnnouncing()
    {
        _tracker.Set(MainClub, 5);
        ClubReportsLevel(_mainClient, MainClub, 6);
        _clubs.ReadForUpdateByIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns((global::Entities.Club?)null);

        await CreateHandler().Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _notifier.DidNotReceiveWithAnyArgs().SendClubLevelUpEvent(default!, default);
        _tracker.TryGet(MainClub).Should().Be(6);
    }

    [Fact]
    public async Task EveryConfiguredClub_IsReadThroughItsOwnClient()
    {
        _tracker.Set(MainClub, 1);
        _tracker.Set(SecondClub, 1);
        ClubReportsLevel(_mainClient, MainClub, 1);
        ClubReportsLevel(_secondClient, SecondClub, 1);

        await CreateHandler(SecondClub).Handle(new CheckClubLevelCommand(), CancellationToken.None);

        await _mainClient.Received(1).ReadClubAsync(MainClub, Arg.Any<CancellationToken>());
        await _secondClient.Received(1).ReadClubAsync(SecondClub, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tracker_IsSeededFromTheDatabase_ForEveryConfiguredClub()
    {
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(MainClub, "Main", 4));
        _clubs.ReadClubByIdAsync(SecondClub, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(SecondClub, "Second", 2));
        ClubReportsLevel(_mainClient, MainClub, 4);
        ClubReportsLevel(_secondClient, SecondClub, 2);

        await CreateHandler(SecondClub).Handle(new CheckClubLevelCommand(), CancellationToken.None);

        // Both were already known at these levels, so nothing looks like a change.
        await _sender.DidNotReceiveWithAnyArgs().Send(default(SetClubLevelStatusCommand)!, default);
        _tracker.TryGet(SecondClub).Should().Be(2);
    }
}
