using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMemberActivity;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

/// <summary>
/// Unit tests for <see cref="ActivityReadHandlers"/>'s <c>GetLastCheckTimeQuery</c> handler. The last
/// check time is read from the main club's <c>LatestActivityCheckTime</c> (recorded by the activity
/// check on every run) — the value that drives which excuses count as relevant (issue #200).
/// </summary>
public sealed class ActivityReadHandlersTests
{
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly Guid _mainClubId = Guid.NewGuid();

    private ActivityReadHandlers CreateHandler()
    {
        var geoGuessrConfig = Options.Create(new GeoGuessrConfiguration
        {
            SyncSchedule = "0 0 0 * * ?",
            ActivityNcfaToken = "x",
            MissionsNcfaToken = "x",
            UserProfileNcfaToken = "x",
            Clubs = [new GeoGuessrClubEntry { ClubId = _mainClubId, NcfaToken = "x", IsMain = true }],
        });
        var missionConfig = Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 0 0 * * ?",
            DefaultMessage = "x",
        });

        return new ActivityReadHandlers(
            _clubs, _clubMembers, _activityReader, geoGuessrConfig, missionConfig,
            NullLogger<ActivityReadHandlers>.Instance);
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsTheMainClubsRecordedCheckTime()
    {
        var checkTime = DateTimeOffset.UtcNow.AddDays(-1);
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1, checkTime));

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().Be(checkTime);
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsNull_WhenTheClubWasNeverChecked()
    {
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>())
            .Returns(Entities.Club.Create(_mainClubId, "main", 1));

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLastCheckTime_ReturnsNull_WhenTheMainClubDoesNotExist()
    {
        _clubs.ReadClubByIdAsync(_mainClubId, Arg.Any<CancellationToken>()).Returns((Entities.Club?)null);

        var result = await CreateHandler().Handle(new GetLastCheckTimeQuery(), CancellationToken.None);

        result.Should().BeNull();
    }
}
