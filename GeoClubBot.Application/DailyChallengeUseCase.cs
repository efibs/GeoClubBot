using System.Text;
using System.Text.Json;
using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases;

public class DailyChallengeUseCase(IGeoGuessrAccess geoGuessrAccess, IMessageSender messageSender, IConfiguration config) : IDailyChallengeUseCase
{
    public async Task CreateDailyChallengeAsync()
    {
        var rng = new Random();

        // Read the challenges configuration file
        var configFileString = await File.ReadAllTextAsync(_challengesConfigurationFilePath);

        // Deserialize
        var challengeConfig = JsonSerializer.Deserialize<List<ClubChallengeConfigurationDifficulty>>(configFileString);

        // If the challenge config ist not correct
        if (challengeConfig == null)
        {
            throw new InvalidOperationException($"Invalid challenge configuration file: {_challengesConfigurationFilePath}");
        }
        
        // Select entries
        var selectedEntries = challengeConfig
            .ToDictionary(
                d => d.Difficulty, 
                d => d.Entries[rng.Next(0, d.Entries.Count)]);
        
        var nextChallenges = new List<ClubChallenge>(selectedEntries.Count);
        foreach (var selectedEntry in selectedEntries)
        {
            // Create the challenge
            var createdChallenge = await geoGuessrAccess.CreateChallengeAsync(new GeoGuessrCreateChallengeRequestDTO
            (
                1,
                0,
                selectedEntry.Value.ForbidMoving,
                selectedEntry.Value.ForbidRotating,
                selectedEntry.Value.ForbidZooming,
                selectedEntry.Value.MapId,
                selectedEntry.Value.TimeLimit
            ));
            
            // Add to the next challenges
            nextChallenges.Add(new ClubChallenge(selectedEntry.Key, selectedEntry.Value.Description, createdChallenge.Token));
        }
        
        // Build the message
        var messageText = _buildMessage(null, nextChallenges);
        
        // Send the message
        await messageSender.SendMessageAsync(messageText, _textChannelId);
    }

    private string _buildMessage(GeoGuessrChallengeResultHighscores? lastChallengeHighscores,
        List<ClubChallenge> nextChallenges)
    {
        var builder = new StringBuilder();
        if (lastChallengeHighscores != null)
        {
            _appendLastChallengeResults(builder, lastChallengeHighscores);
        }

        builder.Append("# :dart: Next challenges :dart:");

        foreach (var nextChallenge in nextChallenges)
        {
            builder.Append("\n - [");
            builder.Append(nextChallenge.Difficulty);
            builder.Append(" (");
            builder.Append(nextChallenge.Description);
            builder.Append(")](https://www.geoguessr.com/challenge/");
            builder.Append(nextChallenge.ChallengeId);
            builder.Append(")");
        }

        return builder.ToString();
    }

    private void _appendLastChallengeResults(StringBuilder builder,
        GeoGuessrChallengeResultHighscores lastChallengeHighscores)
    {
    }

    private readonly string _challengesConfigurationFilePath =
        config.GetValue<string>(ConfigKeys.DailyChallengesConfigurationFilePathConfigurationKey)!;

    private readonly string _textChannelId =
        config.GetValue<string>(ConfigKeys.DailyChallengesTextChannelIdConfigurationKey)!;
}