using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.OutputPorts.Notifications;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Club;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Club;

/// <summary>
/// <see cref="ApplyClubLevelHandler"/> is reached both by the scheduled check (covered through
/// <see cref="CheckClubLevelHandlerTests"/>) and by a level pushed over GeoGuessr's socket; these
/// tests cover what only the push path can bring about.
/// </summary>
public sealed class ApplyClubLevelHandlerTests
{
    private static readonly Guid MainClub = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClubEventNotifier _notifier = Substitute.For<IClubEventNotifier>();
    private readonly ClubLevelTracker _tracker = new();

    private ApplyClubLevelHandler CreateHandler() => new(
        _clubs, _tracker, _sender, _unitOfWork, [_notifier],
        new GeoGuessrConfigurationBuilder().WithClub(MainClub).BuildOptions(),
        NullLogger<ApplyClubLevelHandler>.Instance);

    [Fact]
    public async Task APushBeforeAnyCheck_ComparesWithTheStoredLevel_AndAnnouncesTheLevelUp()
    {
        // The tracker is empty; the stored club says level 4, so 5 is a level-up, not a first sighting.
        _clubs.ReadClubByIdAsync(MainClub, Arg.Any<CancellationToken>())
            .Returns(global::Entities.Club.Create(MainClub, "Main", 4));
        var tracked = global::Entities.Club.Create(MainClub, "Main", 4);
        _clubs.ReadForUpdateByIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns(tracked);

        await CreateHandler().Handle(new ApplyClubLevelCommand(MainClub, 5), CancellationToken.None);

        await _notifier.Received(1).SendClubLevelUpEvent(tracked, Arg.Any<CancellationToken>());
        tracked.Level.Should().Be(5);
    }

    [Fact]
    public async Task TheSameLevelUpArrivingTwiceAtOnce_IsAnnouncedOnce()
    {
        _tracker.Set(MainClub, 4);
        var tracked = global::Entities.Club.Create(MainClub, "Main", 4);
        _clubs.ReadForUpdateByIdAsync(MainClub, Arg.Any<CancellationToken>()).Returns(tracked);

        // The first announcement is held open until the second apply has had every chance to start.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _notifier.SendClubLevelUpEvent(tracked, Arg.Any<CancellationToken>()).Returns(_ => release.Task);

        var first = CreateHandler().Handle(new ApplyClubLevelCommand(MainClub, 5), CancellationToken.None);
        var second = CreateHandler().Handle(new ApplyClubLevelCommand(MainClub, 5), CancellationToken.None);
        await Task.Delay(50);
        release.SetResult();
        await Task.WhenAll(first, second);

        await _notifier.Received(1).SendClubLevelUpEvent(tracked, Arg.Any<CancellationToken>());
    }
}
