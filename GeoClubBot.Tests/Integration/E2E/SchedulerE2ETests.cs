using System.Diagnostics.Metrics;
using Entities;
using FluentAssertions;
using Infrastructure.InputAdapters.Jobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Integration.E2E;

/// <summary>
/// End-to-end coverage of the Quartz scheduler itself: the host boots with the real hosted service
/// running, the scanner's registrations reach a started scheduler, and a job fired by that scheduler
/// resolves its scoped dependencies and writes to the database.
///
/// The other E2E tests strip the scheduler — left running with live cron expressions it fires real
/// jobs against GeoGuessr while the test is working. Here it stays, with every schedule rewritten to
/// a date that never arrives (see <see cref="GeoClubBotApiFactory"/>), so the only thing that ever
/// runs is the job a test triggers by hand.
///
/// This is the layer <see cref="Api.CronJobRegistrationTests"/> cannot reach: that one inspects the
/// registration on a scheduler nobody started, so it says nothing about the hosted service starting,
/// about Quartz resolving a job out of a DI scope, or about the job listener the scheduler attaches.
/// A Quartz major version can change any of those without breaking a build.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SchedulerE2ETests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly Guid _clubId = Guid.NewGuid();
    private readonly string _missionUserId = Guid.NewGuid().ToString("N")[..24];
    private readonly string _challengeUserId = Guid.NewGuid().ToString("N")[..24];
    private readonly string _idleUserId = Guid.NewGuid().ToString("N")[..24];
    private readonly DateOnly _yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    private readonly GeoClubBotApiFactory _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SchedulerE2ETests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _baseFactory = new GeoClubBotApiFactory(fixture.ConnectionString, _clubId, runScheduler: true);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // The snapshot job reads yesterday's club activity feed. Serve it from memory:
                // the point of the test is the path from scheduler to database, not GeoGuessr.
                services.RemoveAll<IGeoGuessrActivityReader>();
                services.AddSingleton<IGeoGuessrActivityReader>(new StubGeoGuessrActivityReader(
                [
                    Activity(_missionUserId, ClubXpActivityKind.DailyMission),
                    Activity(_challengeUserId, ClubXpActivityKind.DailyChallengeOrDuel)
                ]));
            }));

        // Forces the host to start, which is what starts the scheduler.
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Scheduler_starts_with_the_host_and_holds_every_cron_job()
    {
        var scheduler = await GetSchedulerAsync();

        scheduler.Status.Should().Be(SchedulerStatus.Running,
            "the Quartz hosted service should have started the scheduler, not left it in standby");

        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        jobKeys.Should().Contain(new JobKey(nameof(DailyMissionCompletionSnapshotJob)));
    }

    [Fact]
    public async Task Triggering_a_job_runs_it_through_the_scheduler_and_writes_its_result()
    {
        await SeedClubAndMembersAsync();

        var scheduler = await GetSchedulerAsync();
        var jobKey = new JobKey(nameof(DailyMissionCompletionSnapshotJob));

        var executed = await RunToCompletionAsync(scheduler, jobKey);
        executed.Should().BeTrue("the scheduler should have run the triggered job within the timeout");

        await using var db = _fixture.CreateDbContext();
        var rows = await db.Set<DailyMissionMemberCompletion>()
            .Where(c => c.ClubId == _clubId && c.Date == _yesterday)
            .ToListAsync();

        // One row per member, including the member who did nothing — that zero is the denominator
        // the statistics read, so its absence would be a silent hole rather than a missing row.
        rows.Should().HaveCount(3);
        rows.Single(r => r.UserId == _missionUserId).CompletedCount.Should().Be(1);
        rows.Single(r => r.UserId == _challengeUserId).DailyChallengeCount.Should().Be(1);
        rows.Single(r => r.UserId == _idleUserId).CompletedCount.Should().Be(0);
    }

    [Fact]
    public async Task Running_a_job_records_its_duration_in_the_job_metrics()
    {
        await SeedClubAndMembersAsync();

        var durations = new List<double>();
        using var meterListener = ListenForJobDurations(durations);

        var scheduler = await GetSchedulerAsync();
        var executed = await RunToCompletionAsync(scheduler, new JobKey(nameof(DailyMissionCompletionSnapshotJob)));
        executed.Should().BeTrue();

        // QuartzJobMetricsListener is attached by the scheduler, not called by the job, and every
        // IJobListener member has a default implementation — so a listener that stops implementing
        // the interface keeps compiling and silently records nothing.
        durations.Should().NotBeEmpty("the job listener should have recorded the execution");
    }

    private async Task<IScheduler> GetSchedulerAsync() =>
        await _factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

    /// <summary>
    /// Triggers the job and waits for the scheduler to report it finished. Quartz fires jobs on its
    /// own threads, so the completion signal comes from a listener rather than from the trigger call.
    /// </summary>
    private static async Task<bool> RunToCompletionAsync(IScheduler scheduler, JobKey jobKey)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new JobCompletionListener(jobKey, completed);

        scheduler.ListenerManager.AddJobListener(listener, [Matchers.Key(jobKey)]);

        try
        {
            await scheduler.TriggerJob(jobKey);

            var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            return finished == completed.Task;
        }
        finally
        {
            scheduler.ListenerManager.RemoveJobListener(listener.Name);
        }
    }

    private static MeterListener ListenForJobDurations(List<double> durations)
    {
        var listener = new MeterListener();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "geoclubbot.job.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) =>
        {
            lock (durations)
            {
                durations.Add(measurement);
            }
        });

        listener.Start();
        return listener;
    }

    private async Task SeedClubAndMembersAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.Add(Club.Create(_clubId, $"Club-{Guid.NewGuid():N}"[..20], level: 1));

        foreach (var userId in new[] { _missionUserId, _challengeUserId, _idleUserId })
        {
            var user = GeoGuessrUser.Create(userId, $"User-{userId[..8]}", (ulong)Random.Shared.NextInt64(1, long.MaxValue));
            db.Add(ClubMember.Create(user, _clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddDays(-30)));
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Midday yesterday, so the entry lands inside the day the job snapshots.</summary>
    private ReadClubActivitiesItemDto Activity(string userId, ClubXpActivityKind kind) => new()
    {
        UserId = userId,
        Type = (int)kind,
        XpReward = 20,
        RecordedAt = new DateTimeOffset(_yesterday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero)
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    private sealed class JobCompletionListener(JobKey jobKey, TaskCompletionSource completed) : IJobListener
    {
        public string Name { get; } = $"completion-{jobKey.Name}-{Guid.NewGuid():N}";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            completed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            completed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubGeoGuessrActivityReader(IReadOnlyList<ReadClubActivitiesItemDto> activities)
        : IGeoGuessrActivityReader
    {
        public Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadTodaysActivitiesAsync(
            Guid clubId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadClubActivitiesItemDto>>([]);

        public Task<IReadOnlyList<ReadClubActivitiesItemDto>> ReadActivitiesSinceAsync(
            Guid clubId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(activities);
    }
}
