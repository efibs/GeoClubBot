using System.Text.Json;
using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class FileExcusesRepository : IExcusesRepository
{
    private static readonly string DataFolderPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "data");
    private static readonly string ExcusesFileName = Path.Combine(DataFolderPath, "excuses.json");

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
    
    public async Task<Dictionary<string, List<GeoGuessrClubMemberExcuse>>> ReadExcusesAsync()
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

    public async Task WriteExcuseAsync(string memberNickname, GeoGuessrClubMemberExcuse excuse)
    {
        // Read the excuses file
        var existingExcusesJson = await File.ReadAllTextAsync(ExcusesFileName);
        
        // Parse from json
        var existingExcuses = JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(existingExcusesJson);
        
        // Sanity check
        if (existingExcuses == null)
        {
            throw new InvalidOperationException("Excuses are malformed");
        }
        
        // Create the new excuses dictionary
        var newExcusesDict =  new Dictionary<string, List<GeoGuessrClubMemberExcuse>>(existingExcuses);
        
        // Get the existing excuses of the player
        existingExcuses.TryGetValue(memberNickname, out var existingExcusesOfPlayer);
        existingExcusesOfPlayer ??= [];

        newExcusesDict[memberNickname] = existingExcusesOfPlayer.Append(excuse).ToList();
        
        // Serialize new excuses
        var newExcusesJson = JsonSerializer.Serialize(newExcusesDict);
        
        // Write new excuses to file
        await File.WriteAllTextAsync(ExcusesFileName, newExcusesJson);
    }

    public async Task<bool> DeleteExcuseAsync(Guid excuseId)
    {
        // Read the excuses file
        var existingExcusesJson = await File.ReadAllTextAsync(ExcusesFileName);
        
        // Parse from json
        var existingExcuses = JsonSerializer.Deserialize<Dictionary<string, List<GeoGuessrClubMemberExcuse>>>(existingExcusesJson);
        
        // Sanity check
        if (existingExcuses == null)
        {
            throw new InvalidOperationException("Excuses are malformed");
        }

        // Create the new excuses dictionary
        var newExcusesDict =  new Dictionary<string, List<GeoGuessrClubMemberExcuse>>(existingExcuses);
        
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
        newExcusesDict[memberNickname] = existingExcusesOfPlayer.Where(e =>e.Id != excuseId).ToList();
        
        // Serialize new excuses
        var newExcusesJson = JsonSerializer.Serialize(newExcusesDict);
        
        // Write new excuses to file
        await File.WriteAllTextAsync(ExcusesFileName, newExcusesJson);
        
        return true;
    }
}