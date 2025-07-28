using System.Text;
using System.Text.Json;
using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases;

public class DailyChallengeUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IClubChallengeRepository clubChallengeRepository,
    IMessageSender messageSender,
    IConfiguration config,
    ILogger<DailyChallengeUseCase> logger) : IDailyChallengeUseCase
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
            throw new InvalidOperationException(
                $"Invalid challenge configuration file: {_challengesConfigurationFilePath}");
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
            nextChallenges.Add(new ClubChallenge(selectedEntry.Key, selectedEntry.Value.Description,
                createdChallenge.Token));
        }

        // Read the old club challenges
        var oldClubChallenges = await clubChallengeRepository.ReadLatestClubChallengeLinksAsync();
        
        // Convert to club challenges link
        var clubChallengeLinks = nextChallenges.Select(c => new ClubChallengeLink
        {
            Difficulty = c.Difficulty,
            ChallengeId = c.ChallengeId,
        }).ToList();
        
        // Create the new club challenges
        await clubChallengeRepository.CreateLatestClubChallengeLinksAsync(clubChallengeLinks);

        // Get the last challenge high scores
        var lastChallengeHighScores = await _readLastChallengeHighScoresAsync(oldClubChallenges);
        
        // Build the message
        var messageText = _buildMessage(lastChallengeHighScores, nextChallenges);

        // Send the message
        await messageSender.SendMessageAsync(messageText, _textChannelId);
        
        // Delete the old club challenges
        await clubChallengeRepository.DeleteLatestClubChallengeLinksAsync(oldClubChallenges.Select(c => c.Id).ToList());
    }

    private string _buildMessage(List<ClubChallengeResult>? lastChallengeResults,
        List<ClubChallenge> nextChallenges)
    {
        var builder = new StringBuilder();
        if (lastChallengeResults is { Count: > 0 })
        {
            _appendLastChallengeResults(builder, lastChallengeResults);
        }

        builder.Append("\n# :dart: Next challenges :dart:");

        foreach (var nextChallenge in nextChallenges)
        {
            // If the challenge could not be retrieved
            if (nextChallenge.ChallengeId == string.Empty)
            {
                builder.Append("\n - ");
                builder.Append(nextChallenge.Difficulty);
                builder.Append(": ERROR");
                continue;
            }
            
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
        List<ClubChallengeResult> lastChallengeResults)
    {
        builder.Append("# :trophy: The results are in! :trophy: ");

        foreach (var lastChallengeResult in lastChallengeResults)
        {
            builder.Append("\n## ");
            builder.Append(lastChallengeResult.Difficulty);

            if (!lastChallengeResult.Players.Any())
            {
                builder.AppendLine();
                builder.Append("No one participated :frowning2: ");
                continue;
            }

            var place = 1;
            foreach (var player in lastChallengeResult.Players)
            {
                builder.AppendLine();
                switch (place++)
                {
                    case 1:
                        builder.Append(":first_place:");
                        break;
                    case 2:
                        builder.Append(":second_place:");
                        break;
                    case 3:
                        builder.Append(":third_place:");
                        break;
                    default:
                        builder.Append(place);
                        builder.Append(". ");
                        break;
                }
                builder.Append(player.Nickname);
                builder.Append(" (");
                builder.Append(player.TotalScore);
                builder.Append(", ");
                builder.Append(player.TotalDistance);
                builder.Append(')');
            }
        }
    }

    private async Task<List<ClubChallengeResult>> _readLastChallengeHighScoresAsync(List<ClubChallengeLink> oldChallengeLinks)
    {
        var results = new List<ClubChallengeResult>();

        foreach (var oldChallengeLink in oldChallengeLinks)
        {
            // Read the highscores 
            var highscores = await geoGuessrAccess.ReadHighscoresAsync(oldChallengeLink.ChallengeId, 10, 5);
            
            // If the highscores could not be retrieved
            if (highscores == null)
            {
                logger.LogWarning($"Challenge with id {oldChallengeLink.ChallengeId} not found");
                continue;
            }
            
            // Map the players
            var players = highscores.Items
                .Select(s => new ClubChallengeResultPlayer(
                    s.Game.Player.Nick, 
                    $"{s.Game.Player.TotalScore.Amount} {s.Game.Player.TotalScore.Unit}",
                    $"{s.Game.Player.TotalDistance.Meters.Amount}{s.Game.Player.TotalDistance.Meters.Unit}"))
                .ToList();
            
            // Build the result object
            var result = new ClubChallengeResult(oldChallengeLink.Difficulty, players);
            
            results.Add(result);
        }

        return results;
    }
    
    private readonly string _challengesConfigurationFilePath =
        config.GetValue<string>(ConfigKeys.DailyChallengesConfigurationFilePathConfigurationKey)!;

    private readonly string _textChannelId =
        config.GetValue<string>(ConfigKeys.DailyChallengesTextChannelIdConfigurationKey)!;
}