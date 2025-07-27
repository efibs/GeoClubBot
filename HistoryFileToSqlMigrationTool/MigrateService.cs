using System.Text.Json;
using Entities;
using Infrastructure.OutputAdapters.DataAccess;

namespace HistoryFileToSqlMigrationTool;

public class MigrateService(IServiceProvider serviceProvider, ILogger<MigrateService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Read the old history file contents
        var oldHistoryFileContents = await File.ReadAllTextAsync("OldHistory.json");

        // Parse objects
        var oldHistory = JsonSerializer.Deserialize<Dictionary<string, List<OldHistoryEntry>>>(oldHistoryFileContents);

        logger.LogInformation(
            $"Migrating {oldHistory!.SelectMany(e => e.Value).Count()} history entries for {oldHistory!.Count} users...");

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GeoClubBotDbContext>();

        foreach (var oldHistoryEntry in oldHistory)
        {
            // Find the club member
            var clubMember = await dbContext.ClubMembers.FindAsync(oldHistoryEntry.Key);

            // If the club member could not be found
            if (clubMember == null)
            {
                logger.LogInformation(
                    $"Member {oldHistoryEntry.Value.First().Nickname} (Id: {oldHistoryEntry.Key}) not found. Skipping his history.");
                continue;
            }

            // Map old history entries and add to db context
            dbContext.AddRange(oldHistoryEntry.Value.Select(e => new ClubMemberHistoryEntry
            {
                ClubMember = clubMember,
                Timestamp = e.Timestamp,
                UserId = clubMember.UserId,
                Xp = e.Xp
            }));
        }

        await dbContext.SaveChangesAsync();

        logger.LogInformation($"Migrated {oldHistory!.SelectMany(e => e.Value).Count()} history entries for {oldHistory!.Count} users.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}