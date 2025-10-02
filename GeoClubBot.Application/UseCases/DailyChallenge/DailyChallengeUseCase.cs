using System.Text;
using System.Text.Json;
using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.DailyChallenge;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace UseCases.UseCases.DailyChallenge;

public class DailyChallengeUseCase(
    IGeoGuessrAccess geoGuessrAccess,
    IClubChallengeRepository clubChallengeRepository,
    IMessageSender messageSender,
    IDistributeDailyChallengeRolesUseCase distributeDailyChallengeRolesUseCase,
    IConfiguration config,
    ILogger<DailyChallengeUseCase> logger) : IDailyChallengeUseCase
{
    public async Task CreateDailyChallengeAsync()
    {
        // Read the challenges configuration file
        var configFileString = await File.ReadAllTextAsync(_challengesConfigurationFilePath).ConfigureAwait(false);

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
                d => d.Entries[_rng.Next(0, d.Entries.Count)]);

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
            )).ConfigureAwait(false);

            // Add to the next challenges
            nextChallenges.Add(new ClubChallenge(selectedEntry.Key, selectedEntry.Value.Description,
                createdChallenge.Token));
        }

        // Read the old club challenges
        var oldClubChallenges = await clubChallengeRepository.ReadLatestClubChallengeLinksAsync().ConfigureAwait(false);
        
        // Convert to club challenges link
        var clubChallengeLinks = nextChallenges.Select(c => new ClubChallengeLink
        {
            Difficulty = c.Difficulty,
            ChallengeId = c.ChallengeId,
        }).ToList();
        
        // Create the new club challenges
        await clubChallengeRepository.CreateLatestClubChallengeLinksAsync(clubChallengeLinks).ConfigureAwait(false);

        // Get the last challenge high scores
        var lastChallengeHighScores = await _readLastChallengeHighScoresAsync(oldClubChallenges).ConfigureAwait(false);

        // Delete the old club challenges
        await clubChallengeRepository.DeleteLatestClubChallengeLinksAsync(oldClubChallenges.Select(c => c.Id).ToList()).ConfigureAwait(false);
        
        // Send the messages
        await _sendMessagesAsync(lastChallengeHighScores, nextChallenges).ConfigureAwait(false);
        
        // Distribute the roles
        await distributeDailyChallengeRolesUseCase.DistributeDailyChallengeRolesAsync(lastChallengeHighScores).ConfigureAwait(false);
    }

    private async Task<List<ClubChallengeResult>> _readLastChallengeHighScoresAsync(List<ClubChallengeLink> oldChallengeLinks)
    {
        var results = new List<ClubChallengeResult>();

        foreach (var oldChallengeLink in oldChallengeLinks)
        {
            // Read the highscores 
            var highscores = await geoGuessrAccess.ReadHighscoresAsync(oldChallengeLink.ChallengeId, 10, 5).ConfigureAwait(false);
            
            // If the highscores could not be retrieved
            if (highscores == null)
            {
                logger.LogWarning($"Challenge with id {oldChallengeLink.ChallengeId} not found");
                continue;
            }
            
            // Map the players
            var players = highscores.Items
                .Select(s => new ClubChallengeResultPlayer(
                    s.Game.Player.Id,
                    s.Game.Player.Nick, 
                    $"{s.Game.Player.TotalScore.Amount} {s.Game.Player.TotalScore.Unit}",
                    $"{s.Game.Player.TotalDistance.Meters.Amount}{s.Game.Player.TotalDistance.Meters.Unit}"))
                .ToList();
            
            // Build the result object
            var result = new ClubChallengeResult(oldChallengeLink.Difficulty, oldChallengeLink.RolePriority, players);
            
            results.Add(result);
        }

        return results;
    }

    private async Task _sendMessagesAsync(List<ClubChallengeResult> lastChallengeHighScores, List<ClubChallenge> nextChallenges)
    {
        // If there are last challenges
        if (lastChallengeHighScores is { Count: > 0 })
        {
            // Send the last challenge results
            await _sendLastChallengeResults(lastChallengeHighScores).ConfigureAwait(false);
        }
        
        // Send the next challenges
        await _sendNextChallengesAsync(nextChallenges).ConfigureAwait(false);
    }
    
    private async Task _sendLastChallengeResults(List<ClubChallengeResult> lastChallengeResults)
    {
        var builder = new StringBuilder("# :trophy: The results are in! :trophy: ");

        foreach (var lastChallengeResult in lastChallengeResults)
        {
            builder.Append("\n## ");
            builder.Append(lastChallengeResult.Difficulty);

            if (!lastChallengeResult.Players.Any())
            {
                builder.AppendLine();
                builder.Append("No one participated :frowning2: ");
            }
            else
            {
                // Append the players
                _appendPlayers(builder, lastChallengeResult.Players);
            }

            // Send the result
            await  messageSender.SendMessageAsync(builder.ToString(), _textChannelId).ConfigureAwait(false);
            
            // Reset the string builder
            builder = new StringBuilder();
        }
    }

    private void _appendPlayers(StringBuilder builder, List<ClubChallengeResultPlayer> players)
    {
        var place = 1;
        foreach (var player in players)
        {
            builder.AppendLine();
            switch (place)
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

            place++;
        }
    }
    
    private async Task _sendNextChallengesAsync(List<ClubChallenge> nextChallenges)
    {
        var builder = new StringBuilder("# :dart: Next challenges :dart:");

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

        // Send the next challenges
        await  messageSender.SendMessageAsync(builder.ToString(), _textChannelId).ConfigureAwait(false);
    }
    
    private readonly string _challengesConfigurationFilePath =
        config.GetValue<string>(ConfigKeys.DailyChallengesConfigurationFilePathConfigurationKey)!;

    private readonly string _textChannelId =
        config.GetValue<string>(ConfigKeys.DailyChallengesTextChannelIdConfigurationKey)!;
    
    private static readonly Random _rng = new Random();
}