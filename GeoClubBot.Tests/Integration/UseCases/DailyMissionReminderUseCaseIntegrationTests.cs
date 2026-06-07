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
/// Exercises the daily-mission-reminder use cases (set / stop / status / send-due) through the
/// real MediatR pipeline against the shared Postgres container.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyMissionReminderUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    [Fact]
    public async Task SetReminder_CreatesANewReminder()
    {
        var discordId = NewDiscordId();
        var time = new TimeOnly(8, 30);

        using var host = CreateHost();
        await host.SendAsync(new SetDailyMissionReminderCommand(discordId, time, null, "wake up"));

        var status = await host.SendAsync(new GetDailyMissionReminderStatusQuery(discordId));
        status.Should().NotBeNull();
        status!.ReminderTimeUtc.Should().Be(time);
        status.CustomMessage.Should().Be("wake up");
    }

    [Fact]
    public async Task SetReminder_UpdatesAnExistingReminder()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new SetDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, "first"));
        await host.SendAsync(new SetDailyMissionReminderCommand(discordId, new TimeOnly(9, 15), null, "second"));

        var status = await host.SendAsync(new GetDailyMissionReminderStatusQuery(discordId));
        status.Should().NotBeNull();
        status!.ReminderTimeUtc.Should().Be(new TimeOnly(9, 15));
        status.CustomMessage.Should().Be("second");
    }

    [Fact]
    public async Task SetReminder_ThrowsValidationException_ForZeroDiscordUserId()
    {
        using var host = CreateHost();

        var act = () => host.SendAsync(new SetDailyMissionReminderCommand(0, new TimeOnly(8, 0), null, null));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task StopReminder_RemovesAnExistingReminder()
    {
        var discordId = NewDiscordId();

        using var host = CreateHost();
        await host.SendAsync(new SetDailyMissionReminderCommand(discordId, new TimeOnly(8, 0), null, null));

        var result = await host.SendAsync(new StopDailyMissionReminderCommand(discordId));

        result.IsSuccess.Should().BeTrue();
        (await host.SendAsync(new GetDailyMissionReminderStatusQuery(discordId))).Should().BeNull();
    }

    [Fact]
    public async Task StopReminder_ReturnsNotFound_WhenNoneConfigured()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new StopDailyMissionReminderCommand(NewDiscordId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetStatus_ReturnsNull_WhenNoneConfigured()
    {
        using var host = CreateHost();

        var status = await host.SendAsync(new GetDailyMissionReminderStatusQuery(NewDiscordId()));

        status.Should().BeNull();
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

        (await host.SendAsync(new GetDailyMissionReminderStatusQuery(discordId))).Should().BeNull();
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
