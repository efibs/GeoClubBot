using System.Text;
using System.Text.Json;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.Assemblers;

namespace UseCases.UseCases.DailyChallenge;

public sealed record DailyChallengeCommand : ICommand;

public sealed class DailyChallengeHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IClubChallengeRepository clubChallenges,
    IDiscordMessageAccess discordMessageAccess,
    ISender mediator,
    IOptions<DailyChallengesConfiguration> config,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig) : IRequestHandler<DailyChallengeCommand, Unit>
{
    private static readonly Random Rng = new();

    // Challenges are always created on behalf of the main club's account.
    private IGeoGuessrClient GeoGuessrClient =>
        _geoGuessrClient ??= geoGuessrClientFactory.CreateClient(geoGuessrConfig.Value.MainClub.ClubId);
    private IGeoGuessrClient? _geoGuessrClient;

    public async Task<Unit> Handle(DailyChallengeCommand request, CancellationToken cancellationToken)
    {
        var configFileString = await File.ReadAllTextAsync(config.Value.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
        var challengeConfig = JsonSerializer.Deserialize<List<ClubChallengeConfigurationDifficulty>>(configFileString);

        if (challengeConfig is null)
        {
            throw new InvalidOperationException(
                $"Invalid challenge configuration file: {config.Value.ConfigurationFilePath}");
        }

        var selectedEntries = challengeConfig
            .ToDictionary(d => d, d => d.Entries[Rng.Next(0, d.Entries.Count)]);

        var nextChallenges = new List<ClubChallenge>(selectedEntries.Count);
        foreach (var selectedEntry in selectedEntries)
        {
            var apiRequest = new PostChallengeRequestDto
            {
                AccessLevel = 1,
                ChallengeType = 0,
                ForbidMoving = selectedEntry.Value.ForbidMoving,
                ForbidRotating = selectedEntry.Value.ForbidRotating,
                ForbidZooming = selectedEntry.Value.ForbidZooming,
                Map = selectedEntry.Value.MapId,
                TimeLimit = selectedEntry.Value.TimeLimit
            };

            var response = await GeoGuessrClient.CreateChallengeAsync(apiRequest).ConfigureAwait(false);

            nextChallenges.Add(new ClubChallenge(
                selectedEntry.Key.Difficulty,
                selectedEntry.Key.RolePriority,
                selectedEntry.Value.Description,
                response.Token));
        }

        var oldClubChallenges = await clubChallenges.ReadLatestClubChallengeLinksAsync().ConfigureAwait(false);

        var newLinks = nextChallenges
            .Select(c => ClubChallengeLink.Create(c.Difficulty, c.RolePriority, c.ChallengeId))
            .ToList();
        clubChallenges.AddLatestClubChallengeLinks(newLinks);

        var lastChallengeHighScores = await ReadLastChallengeHighScoresAsync(oldClubChallenges).ConfigureAwait(false);

        clubChallenges.DeleteLatestClubChallengeLinks(oldClubChallenges);

        await SendMessagesAsync(lastChallengeHighScores, nextChallenges).ConfigureAwait(false);

        await mediator
            .Send(new DistributeDailyChallengeRolesCommand(lastChallengeHighScores), cancellationToken)
            .ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task<List<ClubChallengeResult>> ReadLastChallengeHighScoresAsync(List<ClubChallengeLink> oldChallengeLinks)
    {
        var results = new List<ClubChallengeResult>();

        foreach (var oldChallengeLink in oldChallengeLinks)
        {
            var queryParams = new ReadHighscoresQueryParams { Limit = 10, MinRounds = 5 };
            var response = await GeoGuessrClient
                .ReadHighscoresAsync(oldChallengeLink.ChallengeId, queryParams)
                .ConfigureAwait(false);

            var highscores = ChallengeResultHighScoresAssembler.AssembleEntities(response);
            results.Add(new ClubChallengeResult(oldChallengeLink.Difficulty, oldChallengeLink.RolePriority, highscores));
        }

        return results;
    }

    private async Task SendMessagesAsync(List<ClubChallengeResult> lastChallengeHighScores, List<ClubChallenge> nextChallenges)
    {
        if (lastChallengeHighScores is { Count: > 0 })
        {
            await SendLastChallengeResultsAsync(lastChallengeHighScores).ConfigureAwait(false);
        }

        await SendNextChallengesAsync(nextChallenges).ConfigureAwait(false);
    }

    private async Task SendLastChallengeResultsAsync(List<ClubChallengeResult> lastChallengeResults)
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
                AppendPlayers(builder, lastChallengeResult.Players);
            }

            await discordMessageAccess
                .SendMessageAsync(builder.ToString(), config.Value.TextChannelId)
                .ConfigureAwait(false);

            builder = new StringBuilder();
        }
    }

    private static void AppendPlayers(StringBuilder builder, List<ClubChallengeResultPlayer> players)
    {
        var place = 1;
        foreach (var player in players)
        {
            builder.AppendLine();
            switch (place)
            {
                case 1: builder.Append(":first_place:"); break;
                case 2: builder.Append(":second_place:"); break;
                case 3: builder.Append(":third_place:"); break;
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

    private async Task SendNextChallengesAsync(List<ClubChallenge> nextChallenges)
    {
        var builder = new StringBuilder("# :dart: Next challenges :dart:");

        foreach (var nextChallenge in nextChallenges)
        {
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

        await discordMessageAccess
            .SendMessageAsync(builder.ToString(), config.Value.TextChannelId)
            .ConfigureAwait(false);
    }
}
