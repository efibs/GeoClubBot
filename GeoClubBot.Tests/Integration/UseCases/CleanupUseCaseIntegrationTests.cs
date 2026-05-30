using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UseCases.UseCases.Organization;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the organization cleanup use case (purging expired excuses, old history entries, and
/// members left without history or strikes) through the real MediatR pipeline against the shared
/// Postgres container.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CleanupUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost(TimeSpan historyKeep) =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new ActivityCheckerConfiguration
            {
                Schedule = "0 0 0 * * ?",
                TextChannelId = 1UL,
                MinXP = 100,
                GracePeriodDays = 7,
                MaxNumStrikes = 3,
                HistoryKeepTimeSpan = historyKeep,
                StrikeDecayTimeSpan = TimeSpan.FromDays(60),
            })));

    [Fact]
    public async Task Cleanup_RemovesExpiredData_AndKeepsRecentData()
    {
        var historyKeep = TimeSpan.FromDays(30);
        var threshold = DateTimeOffset.UtcNow - historyKeep;

        var clubId = Guid.NewGuid();
        var orphanUserId = NewUserId();
        var keptUserId = NewUserId();

        var oldExcuse = ClubMemberExcuse.Create(keptUserId, threshold.AddDays(-20), threshold.AddDays(-10));
        var recentExcuse = ClubMemberExcuse.Create(keptUserId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
        var oldHistory = ClubMemberHistoryEntry.Create(keptUserId, clubId, xp: 100, threshold.AddDays(-10));
        var recentHistory = ClubMemberHistoryEntry.Create(keptUserId, clubId, xp: 200, DateTimeOffset.UtcNow.AddDays(-1));

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));

            // Orphan: no history, no strikes → should be deleted.
            var orphanUser = GeoGuessrUser.Create(orphanUserId, NewNickname());
            seed.Add(orphanUser);
            seed.Add(ClubMember.Create(orphanUser, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2)));

            // Kept: has a recent history entry → should survive the member purge.
            var keptUser = GeoGuessrUser.Create(keptUserId, NewNickname());
            seed.Add(keptUser);
            var keptMember = ClubMember.Create(keptUser, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-2));
            keptMember.Excuses.Add(oldExcuse);
            keptMember.Excuses.Add(recentExcuse);
            seed.Add(keptMember);

            seed.Add(oldHistory);
            seed.Add(recentHistory);

            await seed.SaveChangesAsync();
        }

        using var host = CreateHost(historyKeep);
        await host.SendAsync(new CleanupCommand());

        await using var read = fixture.CreateDbContext();
        var excuses = new EfExcusesRepository(read);
        var members = new EfClubMemberRepository(read);
        var history = new EfHistoryRepository(read);

        (await excuses.ReadExcuseAsync(oldExcuse.ExcuseId)).Should().BeNull("its To is before the retention threshold");
        (await excuses.ReadExcuseAsync(recentExcuse.ExcuseId)).Should().NotBeNull("it is still active");

        (await members.ReadClubMemberByUserIdAsync(orphanUserId)).Should().BeNull("it has no history or strikes");
        (await members.ReadClubMemberByUserIdAsync(keptUserId)).Should().NotBeNull("it still has a recent history entry");

        var remainingHistory = await history.ReadHistoryEntriesByClubIdAsync(clubId);
        remainingHistory.Should().ContainSingle("only the recent history entry of this club's members should survive");
        remainingHistory[0].Xp.Should().Be(200);
    }
}
