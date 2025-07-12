using System.Text.Json;
using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

/// <summary>
/// Repository to persist the activity on the filesystem.
/// </summary>
public class FileActivityRepository : IActivityRepository
{
    private static readonly string DataFolderPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "data");
    private static readonly string ActivityHistoryFileName = Path.Combine(DataFolderPath, "ActivityHistory.json");
    private static readonly string LatestActivityFileName = Path.Combine(DataFolderPath, "LatestActivity.json");
    private static readonly string StatusesFileName = Path.Combine(DataFolderPath, "Statuses.json");

    public FileActivityRepository()
    {
        // Create the directories if they do not exist
        Directory.CreateDirectory(DataFolderPath);
        
        // Check if the activity history file exists
        if (!File.Exists(ActivityHistoryFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(ActivityHistoryFileName, "{}");
        }

        // Check if the latest activity file exists
        if (!File.Exists(LatestActivityFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(LatestActivityFileName, "{}");
        }
        
        // Check if the statuses file exists
        if (!File.Exists(StatusesFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(StatusesFileName, "{}");
        }
    }

    public async Task WriteActivityEntriesAsync(Dictionary<Guid, GeoGuessrClubMemberActivityEntry> entries)
    {
        // Read the activity history file
        var currentActivityHistoryJson = await File.ReadAllTextAsync(ActivityHistoryFileName).ConfigureAwait(false);

        // Parse from json
        var currentActivityHistory =
            JsonSerializer.Deserialize<Dictionary<Guid, List<GeoGuessrClubMemberActivityEntry>>>(
                currentActivityHistoryJson);

        // Sanity check
        if (currentActivityHistory == null)
        {
            throw new InvalidOperationException("Activity history is malformed");
        }

        // Read the latest activity file
        var currentLatestActivityJson = await File.ReadAllTextAsync(LatestActivityFileName).ConfigureAwait(false);

        // Parse from json
        var currentLatestActivity =
            JsonSerializer.Deserialize<Dictionary<Guid, GeoGuessrClubMemberActivityEntry>>(
                currentLatestActivityJson);

        // Sanity check
        if (currentLatestActivity == null)
        {
            throw new InvalidOperationException("Latest activity is malformed");
        }

        // Get a list of all user ids past and current members
        var userIds = currentActivityHistory.Keys.ToList().Union(entries.Keys.ToList());

        // Create the new activity history. Append the new entry to the list if the user has 
        // a new entry. Otherwise, just leave the entries unmodified.
        var newActivityHistory = userIds.ToDictionary(uId => uId,
            uId => !entries.TryGetValue(uId, out var entry)
                ? currentActivityHistory[uId]
                : currentActivityHistory[uId].Append(entry));

        // Convert the new activity history to json
        var newActivityHistoryJson = JsonSerializer.Serialize(newActivityHistory);

        // Write the new activity history
        await File.WriteAllTextAsync(ActivityHistoryFileName, newActivityHistoryJson)
            .ConfigureAwait(false);

        // Update the latest activity
        var newLatestActivity = new Dictionary<Guid, GeoGuessrClubMemberActivityEntry>(currentLatestActivity);
        foreach (var entry in entries)
        {
            newLatestActivity[entry.Key] = entry.Value;
        }

        // Convert the new latest activity to json
        var newLatestActivityJson = JsonSerializer.Serialize(newLatestActivity);

        // Write the new latest activity
        await File.WriteAllTextAsync(LatestActivityFileName, newLatestActivityJson)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<Guid, GeoGuessrClubMemberActivityEntry>> ReadLatestActivityEntriesAsync()
    {
        // Read the latest activity file
        var latestActivityJson = await File.ReadAllTextAsync(LatestActivityFileName).ConfigureAwait(false);

        // Parse from json
        var latestActivities =
            JsonSerializer.Deserialize<Dictionary<Guid, GeoGuessrClubMemberActivityEntry>>(latestActivityJson);

        return latestActivities ?? new Dictionary<Guid, GeoGuessrClubMemberActivityEntry>();
    }

    public async Task WriteMemberStatusesAsync(Dictionary<Guid, GeoGuessrClubMemberActivityStatus> statuses)
    {
        // Read the statuses file
        var currentStatusesJson = await File.ReadAllTextAsync(StatusesFileName).ConfigureAwait(false);

        // Parse from json
        var currentStatuses =
            JsonSerializer.Deserialize<Dictionary<Guid, GeoGuessrClubMemberActivityStatus>>(
                currentStatusesJson);

        // Sanity check
        if (currentStatuses == null)
        {
            throw new InvalidOperationException("Statuses are malformed");
        }
        
        // Update the statuses
        var newStatuses = new Dictionary<Guid, GeoGuessrClubMemberActivityStatus>(currentStatuses);
        foreach (var entry in statuses)
        {
            newStatuses[entry.Key] = entry.Value;
        }
        
        // Convert the new statuses to json
        var newStatusesJson = JsonSerializer.Serialize(newStatuses);

        // Write the new latest activity
        await File.WriteAllTextAsync(StatusesFileName, newStatusesJson)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<Guid, GeoGuessrClubMemberActivityStatus>> ReadActivityStatusesAsync()
    {
        // Read the latest activity file
        var statusesJson = await File.ReadAllTextAsync(StatusesFileName).ConfigureAwait(false);

        // Parse from json
        var statuses =
            JsonSerializer.Deserialize<Dictionary<Guid, GeoGuessrClubMemberActivityStatus>>(statusesJson);

        return statuses ?? new Dictionary<Guid, GeoGuessrClubMemberActivityStatus>();
    }
}