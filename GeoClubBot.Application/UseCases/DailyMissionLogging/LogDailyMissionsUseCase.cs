using System.Globalization;
using System.Text.RegularExpressions;
using Configuration;
using Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.DailyMissionLogging;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.DailyMissionLogging;

public partial class LogDailyMissionsUseCase(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IUnitOfWork unitOfWork,
    IDiscordMessageAccess discordMessageAccess,
    IOptions<DailyMissionLoggingConfiguration> config,
    ILogger<LogDailyMissionsUseCase> logger) : ILogDailyMissionsUseCase
{
    private static readonly Regex DatePlaceholderRegex =
        new(@"\{\{date:([^}]+)\}\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> GameModeDisplayNames = new()
    {
        ["DailyChallenge"] = "Daily Challenge",
        ["Duels"] = "Duels",
        ["SinglePlayerQuiz"] = "Featured Quiz",
        ["ReplayableQuiz"] = "Quiz",
        ["AnyBattleRoyale"] = "Battle Royale",
        ["Classic"] = "Classic",
        ["TeamDuels"] = "Team Duels",
        ["CommunityStreak"] = "Community Streak",
        ["CountryStreak"] = "Country Streak",
        ["RankedDuels"] = "Ranked Duels",
        ["RankedTeamDuels"] = "Ranked Team Duels",
        ["UnrankedDuels"] = "Unranked Duels",
        ["UnrankedTeamDuels"] = "Unranked Team Duels"
    };

    public async Task LogDailyMissionsAsync()
    {
        var missionsClient = geoGuessrClientFactory.CreateMissionsClient();

        DailyMissionsResponseDto response;
        try
        {
            response = await missionsClient.ReadDailyMissionsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFetchFailed(ex);
            return;
        }

        var fetchedAt = DateTimeOffset.UtcNow;

        var entities = response.Missions.Select(m => new DailyMission
        {
            MissionId = m.Id,
            Type = m.Type,
            GameMode = m.GameMode,
            CurrentProgress = m.CurrentProgress,
            TargetProgress = m.TargetProgress,
            Completed = m.Completed,
            EndDate = m.EndDate,
            RewardAmount = m.RewardAmount,
            RewardType = m.RewardType,
            FetchedAtUtc = fetchedAt
        }).ToList();

        if (entities.Count > 0)
        {
            unitOfWork.DailyMissions.AddRange(entities);
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }

        LogFetched(response.Missions.Count);

        var renderedLines = response.Missions.Select(RenderMission).ToList();
        var missionText = string.Join("\n", renderedLines);

        var readableMessage = RenderTemplate(config.Value.ReadableFormat, missionText);
        var lookupMessage = RenderTemplate(config.Value.LookupFormat, missionText);

        await _trySendAsync(readableMessage, config.Value.ReadableChannelId, "readable").ConfigureAwait(false);
        await _trySendAsync(lookupMessage, config.Value.LookupChannelId, "lookup").ConfigureAwait(false);
    }

    private async Task _trySendAsync(string message, ulong channelId, string channelLabel)
    {
        try
        {
            await discordMessageAccess.SendMessageAsync(message, channelId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogChannelSendFailed(ex, channelLabel, channelId);
        }
    }

    private string RenderMission(DailyMissionDto mission)
    {
        var gameModeDisplay = GameModeDisplayNames.TryGetValue(mission.GameMode, out var display)
            ? display
            : mission.GameMode;

        return mission.Type switch
        {
            "PlayGames" => $"Play {mission.TargetProgress} {gameModeDisplay}",
            "Score" => $"Score {mission.TargetProgress} points in {gameModeDisplay}",
            "WinGames" => $"Win {mission.TargetProgress} {gameModeDisplay}",
            _ => _renderFallback(mission, gameModeDisplay)
        };
    }

    private string _renderFallback(DailyMissionDto mission, string gameModeDisplay)
    {
        LogUnknownMissionType(mission.Type, mission.GameMode);
        return $"{mission.Type} - {gameModeDisplay}";
    }

    private static string RenderTemplate(string template, string missionText)
    {
        var withDate = DatePlaceholderRegex.Replace(template, match =>
            DateTime.UtcNow.ToString(match.Groups[1].Value, CultureInfo.InvariantCulture));

        return withDate.Replace("{{missionText}}", missionText);
    }

    [LoggerMessage(LogLevel.Information, "Fetched {Count} daily missions from GeoGuessr.")]
    partial void LogFetched(int count);

    [LoggerMessage(LogLevel.Error, "Failed to fetch daily missions from GeoGuessr.")]
    partial void LogFetchFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to send daily missions to {Channel} channel {ChannelId}.")]
    partial void LogChannelSendFailed(Exception exception, string channel, ulong channelId);

    [LoggerMessage(LogLevel.Warning, "Unknown daily mission type '{Type}' (gameMode '{GameMode}'); using fallback renderer.")]
    partial void LogUnknownMissionType(string type, string gameMode);
}
