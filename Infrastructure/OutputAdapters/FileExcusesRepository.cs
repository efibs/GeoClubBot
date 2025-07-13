using System.Text.Json;
using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class FileExcusesRepository : IExcusesRepository
{
    private static readonly string DataFolderPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "data");
    private static readonly string ExcusesFileName = Path.Combine(DataFolderPath, "excuses.json");

    private static readonly SemaphoreSlim Lock = new(1, 1);

    public FileExcusesRepository()
    {
        // Create the directories if they do not exist
        Directory.CreateDirectory(DataFolderPath);

        // Check if the excuses file exists
        if (!File.Exists(ExcusesFileName))
        {
            // Create the file with an empty dictionary
            File.WriteAllText(ExcusesFileName, "{}");
        }
    }

    public async Task<List<GeoGuessrClubMemberExcuse>> ReadAllExcusesAsync()
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the excuses from the file
            var excusesJson = await File.ReadAllTextAsync(ExcusesFileName);

            // Parse
            var excusesDict =
                JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(excusesJson);

            // Sanity check
            if (excusesDict == null)
            {
                throw new InvalidOperationException("Excuses are malformed");
            }

            return excusesDict.SelectMany(e => e.Value).ToList();
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task<Dictionary<string, List<GeoGuessrClubMemberExcuse>>> ReadExcusesAsync()
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the excuses file
            var excusesJson = await File.ReadAllTextAsync(ExcusesFileName);

            // Parse from json
            var excuses = JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(excusesJson);

            // Sanity check
            if (excuses == null)
            {
                throw new InvalidOperationException("Excuses are malformed");
            }

            return excuses;
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task WriteExcuseAsync(string memberNickname, GeoGuessrClubMemberExcuse excuse)
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the excuses file
            var existingExcusesJson = await File.ReadAllTextAsync(ExcusesFileName);

            // Parse from json
            var existingExcuses =
                JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(existingExcusesJson);

            // Sanity check
            if (existingExcuses == null)
            {
                throw new InvalidOperationException("Excuses are malformed");
            }

            // Create the new excuses dictionary
            var newExcusesDict = new Dictionary<string, List<GeoGuessrClubMemberExcuse>>(existingExcuses);

            // Get the existing excuses of the player
            existingExcuses.TryGetValue(memberNickname, out var existingExcusesOfPlayer);
            existingExcusesOfPlayer ??= [];

            newExcusesDict[memberNickname] = existingExcusesOfPlayer.Append(excuse).ToList();

            // Serialize new excuses
            var newExcusesJson = JsonSerializer.Serialize(newExcusesDict);

            // Write new excuses to file
            await File.WriteAllTextAsync(ExcusesFileName, newExcusesJson);
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task<bool> DeleteExcuseAsync(Guid excuseId)
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the excuses file
            var existingExcusesJson = await File.ReadAllTextAsync(ExcusesFileName);

            // Parse from json
            var existingExcuses =
                JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(existingExcusesJson);

            // Sanity check
            if (existingExcuses == null)
            {
                throw new InvalidOperationException("Excuses are malformed");
            }

            // Create the new excuses dictionary
            var newExcusesDict = new Dictionary<string, List<GeoGuessrClubMemberExcuse>>(existingExcuses);

            // Find the player of the excuse to be deleted
            var memberNickname = existingExcuses.FirstOrDefault(e => e.Value.Any(e => e.Id == excuseId)).Key;

            // If there is no excuse with that guid
            if (memberNickname == null)
            {
                return false;
            }

            // Get the existing excuses of the player
            var existingExcusesOfPlayer = existingExcuses[memberNickname];

            // Remove excuse with that Id
            newExcusesDict[memberNickname] = existingExcusesOfPlayer.Where(e => e.Id != excuseId).ToList();

            // Serialize new excuses
            var newExcusesJson = JsonSerializer.Serialize(newExcusesDict);

            // Write new excuses to file
            await File.WriteAllTextAsync(ExcusesFileName, newExcusesJson);

            return true;
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }

    public async Task<int> DeleteExcusesAsync(List<Guid> excuseIds)
    {
        // Acquire the lock
        await Lock.WaitAsync();

        try
        {
            // Read the excuses file
            var existingExcusesJson = await File.ReadAllTextAsync(ExcusesFileName);

            // Parse from json
            var existingExcuses =
                JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(existingExcusesJson);

            // Sanity check
            if (existingExcuses == null)
            {
                throw new InvalidOperationException("Excuses are malformed");
            }

            // Create the new excuses dictionary
            var newExcusesDict = new Dictionary<string, List<GeoGuessrClubMemberExcuse>>(existingExcuses);

            // Save how many excuses were deleted
            var numDeletedEntries = 0;

            // For every excuseId to be deleted
            foreach (var excuseId in excuseIds)
            {
                // Find the player of the excuse to be deleted
                var memberNickname = existingExcuses.FirstOrDefault(e => e.Value.Any(e => e.Id == excuseId)).Key;

                // If there is no excuse with that guid
                if (memberNickname == null)
                {
                    continue;
                }

                // Get the existing excuses of the player
                var existingExcusesOfPlayer = existingExcuses[memberNickname];

                // Remove excuse with that Id
                newExcusesDict[memberNickname] = existingExcusesOfPlayer.Where(e => e.Id != excuseId).ToList();

                // Increment num deleted entries
                numDeletedEntries++;
            }

            // Serialize new excuses
            var newExcusesJson = JsonSerializer.Serialize(newExcusesDict);

            // Write new excuses to file
            await File.WriteAllTextAsync(ExcusesFileName, newExcusesJson);

            return numDeletedEntries;
        }
        finally
        {
            // Release the lock
            Lock.Release();
        }
    }
}