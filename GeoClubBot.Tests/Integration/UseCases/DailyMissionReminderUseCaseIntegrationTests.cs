using FluentAssertions;
using FluentValidation;
using NSubstitute;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.DailyMissionReminder;
using Utilities;
using Xunit;
using DomainReminder = Entities.DailyMissionReminder;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the daily-mission-reminder use cases (add / remove / clear / list / send-due) through the
/// real MediatR pipeline against the shared Postgres container.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyMissionReminderUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    [Fact]
    public async Task AddReminder_CreatesANewReminder()
    {
        var discordId = NewDiscordId();
        var time = new TimeOnly(8, 30);

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, time, null, "wake up"));

        var reminders = await host.SendAsync(new ListDailyMissionRemindersQuery(discordId));
        reminders.Should().ContainSingle();
        reminders[0].ReminderTimeUtc.Should().Be(time);
        reminders[0].CustomMessage.Should().Be("wake up");
    }

    [Fact]
    public async Task AddReminder_KeepsSeparateRemindersForDifferentTimes()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, "first"));
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(9, 15), null, "second"));

        var reminders = await host.SendAsync(new ListDailyMissionRemindersQuery(discordId));
        reminders.Should().HaveCount(2);
        reminders.Select(r => r.ReminderTimeUtc).Should().Equal(new TimeOnly(8, 0), new TimeOnly(9, 15));
    }

    [Fact]
    public async Task AddReminder_UpdatesInPlace_WhenAddedAtTheSameTime()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, "first"));
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, "second"));

        var reminders = await host.SendAsync(new ListDailyMissionRemindersQuery(discordId));
        reminders.Should().ContainSingle();
        reminders[0].CustomMessage.Should().Be("second");
    }

    [Fact]
    public async Task AddReminder_ReturnsConflict_WhenAtLimit()
    {
        var discordId = NewDiscordId();

        // The default configured limit is 5.
        using var host = new MediatorTestHost(
            fixture.ConnectionString,
            configurationValues: new Dictionary<string, string?>
            {
                ["DailyMissionReminder:MaxRemindersPerUser"] = "2"
            });

        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, null));
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(9, 0), null, null));

        var result = await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(10, 0), null, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task AddReminder_ThrowsValidationException_ForZeroDiscordUserId()
    {
        using var host = CreateHost();

        var act = () => host.SendAsync(new AddDailyMissionReminderCommand(0, new TimeOnly(8, 0), null, null));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task RemoveReminder_RemovesASingleReminder()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, null));
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(20, 0), null, null));

        var reminders = await host.SendAsync(new ListDailyMissionRemindersQuery(discordId));
        var toRemove = reminders.First(r => r.ReminderTimeUtc == new TimeOnly(8, 0));

        var result = await host.SendAsync(new RemoveDailyMissionReminderCommand(discordId, toRemove.Id));

        result.IsSuccess.Should().BeTrue();
        var remaining = await host.SendAsync(new ListDailyMissionRemindersQuery(discordId));
        remaining.Should().ContainSingle().Which.ReminderTimeUtc.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task RemoveReminder_ReturnsNotFound_WhenUnknownId()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new RemoveDailyMissionReminderCommand(NewDiscordId(), Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RemoveReminder_DoesNotRemoveAnotherUsersReminder()
    {
        var owner = NewDiscordId();
        var stranger = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(owner, new TimeOnly(8, 0), null, null));
        var ownerReminders = await host.SendAsync(new ListDailyMissionRemindersQuery(owner));
        var ownerReminderId = ownerReminders.Single().Id;

        // The stranger tries to remove the owner's reminder by id — scoped by owner, so it is not found.
        var result = await host.SendAsync(new RemoveDailyMissionReminderCommand(stranger, ownerReminderId));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        (await host.SendAsync(new ListDailyMissionRemindersQuery(owner))).Should().ContainSingle();
    }

    [Fact]
    public async Task ClearReminders_RemovesAllRemindersForTheUser()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, null));
        await host.SendAsync(new AddDailyMissionReminderCommand(discordId, new TimeOnly(20, 0), null, null));

        var result = await host.SendAsync(new ClearDailyMissionRemindersCommand(discordId));

        result.IsSuccess.Should().BeTrue();
        (await host.SendAsync(new ListDailyMissionRemindersQuery(discordId))).Should().BeEmpty();
    }

    [Fact]
    public async Task ClearReminders_ReturnsNotFound_WhenNoneConfigured()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new ClearDailyMissionRemindersCommand(NewDiscordId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ListReminders_ReturnsEmpty_WhenNoneConfigured()
    {
        using var host = CreateHost();

        var reminders = await host.SendAsync(new ListDailyMissionRemindersQuery(NewDiscordId()));

        reminders.Should().BeEmpty();
    }

    [Fact]
    public async Task SendDueReminders_DeletesReminder_WhenUserHasLeftTheServer()
    {
        // Seed a reminder that is due right now (matched to the minute, as the handler truncates).
        var discordId = NewDiscordId();
        var now = DateTime.UtcNow;
        var dueNow = new TimeOnly(now.Hour, now.Minute);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainReminder.Create(discordId, dueNow, null, null));
            await seed.SaveChangesAsync();
        }

        // DefaultMessage is [Required] in production; supply it so the handler can build the DM text.
        using var host = new MediatorTestHost(
            fixture.ConnectionString,
            configurationValues: new Dictionary<string, string?>
            {
                ["DailyMissionReminder:Schedule"] = "0 * * ? * * *",
                ["DailyMissionReminder:DefaultMessage"] = "Don't forget your daily mission! {{mission_text}}"
            });
        // Discord reports "no mutual guild" → the user has left the server.
        host.Mock<IDiscordDirectMessageAccess>()
            .SendDirectMessageAsync(discordId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound(DiscordDmErrorCodes.NoMutualGuild, "No mutual guild.")));

        await host.SendAsync(new SendDueRemindersCommand());

        (await host.SendAsync(new ListDailyMissionRemindersQuery(discordId))).Should().BeEmpty();
    }

    [Fact]
    public async Task SendDueReminders_SendsNothing_WhenNoReminderIsDue()
    {
        // Seed a reminder scheduled two hours from now so it is never "due" during the run.
        var discordId = NewDiscordId();
        var notDue = TimeOnly.FromDateTime(DateTime.UtcNow).AddHours(2);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(DomainReminder.Create(discordId, notDue, null, null));
            await seed.SaveChangesAsync();
        }

        using var host = CreateHost();
        await host.SendAsync(new SendDueRemindersCommand());

        await host.Mock<IDiscordDirectMessageAccess>()
            .DidNotReceive()
            .SendDirectMessageAsync(discordId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
