using Entities;
using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Domain;

public sealed class DailyMissionReminderTests
{
    [Fact]
    public void Create_SetsAllFields_AndLeavesLastSentNull()
    {
        var reminder = DailyMissionReminder.Create(
            discordUserId: 1UL,
            reminderTimeUtc: new TimeOnly(8, 30),
            timeZoneId: "Europe/Zurich",
            customMessage: "Go play!");

        reminder.DiscordUserId.Should().Be(1UL);
        reminder.ReminderTimeUtc.Should().Be(new TimeOnly(8, 30));
        reminder.TimeZoneId.Should().Be("Europe/Zurich");
        reminder.CustomMessage.Should().Be("Go play!");
        reminder.LastSentDateUtc.Should().BeNull();
    }

    [Fact]
    public void UpdateSchedule_ChangesFields_AndResetsLastSent()
    {
        var reminder = DailyMissionReminder.Create(1UL, new TimeOnly(8, 0), "UTC", "old");
        reminder.MarkSent(new DateOnly(2025, 1, 1));

        reminder.UpdateSchedule(new TimeOnly(20, 0), "Europe/Berlin", "new");

        reminder.ReminderTimeUtc.Should().Be(new TimeOnly(20, 0));
        reminder.TimeZoneId.Should().Be("Europe/Berlin");
        reminder.CustomMessage.Should().Be("new");
        reminder.LastSentDateUtc.Should().BeNull("rescheduling must allow a reminder to fire again");
    }

    [Fact]
    public void MarkSent_StoresDate()
    {
        var reminder = DailyMissionReminder.Create(1UL, new TimeOnly(8, 0), null, null);

        reminder.MarkSent(new DateOnly(2025, 5, 29));

        reminder.LastSentDateUtc.Should().Be(new DateOnly(2025, 5, 29));
    }

    [Fact]
    public void Create_AllowsNullOptionalFields()
    {
        var reminder = DailyMissionReminder.Create(1UL, new TimeOnly(8, 0), null, null);

        reminder.TimeZoneId.Should().BeNull();
        reminder.CustomMessage.Should().BeNull();
    }
}
