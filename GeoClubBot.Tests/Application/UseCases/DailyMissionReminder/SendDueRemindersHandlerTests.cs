using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.DailyMissionReminder;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Xunit;
using DailyMissionReminderEntity = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Application.UseCases.DailyMissionReminderTests;

public sealed class SendDueRemindersHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const int DailyMissionXpReward = 20;

    private readonly IDailyMissionReminderRepository _reminders = Substitute.For<IDailyMissionReminderRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IDiscordDirectMessageAccess _dm = Substitute.For<IDiscordDirectMessageAccess>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IGeoGuessrActivityReader _activityReader = Substitute.For<IGeoGuessrActivityReader>();
    private readonly ILogger<SendDueRemindersHandler> _logger = Substitute.For<ILogger<SendDueRemindersHandler>>();

    private SendDueRemindersHandler CreateHandler() => new(
        _reminders, _members, _dm, _mediator, _activityReader,
        Options.Create(new DailyMissionReminderConfiguration
        {
            Schedule = "0 * * * * ?",
            DefaultMessage = "Don't forget your daily missions!",
            DailyMissionXpReward = DailyMissionXpReward
        }),
        _logger);

    [Fact]
    public async Task Handle_DoesNothing_WhenNoRemindersDue()
    {
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<DailyMissionReminderEntity>());

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsAlreadyCompletedReminder_AndStillMarksItSent()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                new() { UserId = "user-1", XpReward = DailyMissionXpReward, RecordedAt = DateTimeOffset.UtcNow }
            });

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.DidNotReceive().SendDirectMessageAsync(
            Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_SendsDirectMessage_WhenMissionNotYetCompleted()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, "Custom!");
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        var linkedUser = GeoGuessrUser.Create("user-1", "Player1", 123UL);
        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns(linkedUser);

        var member = new ClubMemberBuilder()
            .WithUserId("user-1").WithDiscordUserId(123UL).InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(member);

        // No matching XpReward in today's activities → mission not completed.
        _activityReader.ReadTodaysActivitiesAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ReadClubActivitiesItemDto>
            {
                new() { UserId = "user-1", XpReward = 99, RecordedAt = DateTimeOffset.UtcNow }
            });

        _dm.SendDirectMessageAsync(123UL, "Custom!", Arg.Any<CancellationToken>())
            .Returns(true);

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        await _dm.Received(1).SendDirectMessageAsync(123UL, "Custom!", Arg.Any<CancellationToken>());
        reminder.LastSentDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_DoesNotMarkSent_WhenDirectMessageFails()
    {
        var reminder = DailyMissionReminderEntity.Create(123UL, new TimeOnly(8, 0), null, null);
        _reminders.ReadDueRemindersForUpdateAsync(
                Arg.Any<TimeOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([reminder]);

        _mediator.Send(Arg.Is<GetLinkedGeoGuessrUserQuery>(q => q.DiscordUserId == 123UL),
            Arg.Any<CancellationToken>()).Returns((GeoGuessrUser?)null);

        _dm.SendDirectMessageAsync(123UL, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await CreateHandler().Handle(new SendDueRemindersCommand(), CancellationToken.None);

        reminder.LastSentDateUtc.Should().BeNull();
    }
}