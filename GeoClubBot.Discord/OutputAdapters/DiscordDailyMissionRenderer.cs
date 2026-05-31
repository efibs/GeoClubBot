using System.Globalization;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Rendering;

namespace GeoClubBot.Discord.OutputAdapters;

/// <summary>
/// Builds the English text for a GeoGuessr daily mission.
///
/// The mission itself is delivered by the API, but its text is generated on the
/// client for internationalization. This is a faithful, self-contained C# port of
/// that client logic (webpack module 441795) with the English ("en") string table
/// inlined, so it depends on nothing but the API data.
///
/// Examples: "Play the Daily Challenge", "Win 5 Team Duels", "Score 25,000 points in Classic".
/// </summary>
public sealed class DiscordDailyMissionRenderer : IDailyMissionRenderer
{
    public string RenderMission(DailyMissionDto mission)
    {
        ArgumentNullException.ThrowIfNull(mission);

        // Plural selector: "one" for a single target, "many" for more than one.
        var plural = mission.TargetProgress > 1 ? "many" : "one";

        // The count, formatted for English (thousands separators, no decimals).
        var formattedProgress = mission.TargetProgress.ToString("N0", _english);

        // First, try a fully-formed, mode-specific phrase.
        // Classic missions with no specific map use a dedicated "score X points" key.
        var specificKey =
            mission.MapSlug == null && mission.GameMode == "Classic"
                ? "components.score-x-points-in-classic"
                : $"components.mission-{mission.Type}-{plural}-{mission.GameMode}".ToLowerInvariant();

        // {0} = formatted count, {1} = map name.
        if (_translations.TryGetValue(specificKey, out var specific))
        {
            return Format(specific, formattedProgress, mission.MapName ?? string.Empty);
        }

        // No mode-specific phrase: fall back to the generic, type-based phrase,
        // filling in the count and the friendly game-mode name.
        if (!_missionTypeKeys.TryGetValue(mission.Type, out var genericKey) || genericKey == null)
        {
            // No fallback available; return the raw key, matching the original behaviour.
            return specificKey;
        }

        var modeName =
            _gameModeNames.GetValueOrDefault(mission.GameMode)
            ?? mission.GameMode;

        // {0} = count, {1} = game-mode name.
        return _translations.TryGetValue(genericKey, out var generic)
            ? Format(generic, mission.TargetProgress.ToString(CultureInfo.InvariantCulture), modeName)
            : genericKey;
    }

    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Substitutes positional {0}/{1} placeholders, mirroring the client i18n interpolation.</summary>
    private static string Format(string template, string arg0, string arg1) =>
        template.Replace("{0}", arg0).Replace("{1}", arg1);

    /// <summary>
    /// Friendly English names for each game mode. Used to fill the generic fallback
    /// phrase (e.g. "Win {count} {gameMode}"). A value of <c>null</c> ("None") means
    /// the mode has no display name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> _gameModeNames =
        new Dictionary<string, string?>
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
            ["UnrankedTeamDuels"] = "Unranked Team Duels",
            ["None"] = null,
        };

    /// <summary>
    /// The generic, type-based translation key used as a fallback when no
    /// mode-specific phrase exists. A value of <c>null</c> ("Unknown") means
    /// no fallback is available and the raw key is returned instead.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> _missionTypeKeys =
        new Dictionary<string, string?>
        {
            ["PlayGames"] = "components.mission-type-play-games",
            ["Score"] = "components.mission-type-score",
            ["WinGames"] = "components.mission-type-win-games",
            ["Unknown"] = null,
        };

    /// <summary>
    /// The English ("en") string table for every mission key the generator can produce,
    /// copied verbatim from GeoGuessr's translation bundle. <c>{0}</c> and <c>{1}</c>
    /// are positional interpolation slots.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _translations =
        new Dictionary<string, string>
        {
            // Generic, type-based fallbacks.
            ["components.mission-type-play-games"] = "Play {0} {1}",
            ["components.mission-type-score"] = "Score {0} points in {1}",
            ["components.mission-type-win-games"] = "Win {0} {1}",

            // Classic, no specific map.
            ["components.score-x-points-in-classic"] = "Score {0} points in Classic",

            // PlayGames — singular.
            ["components.mission-playgames-one-dailychallenge"] = "Play the Daily Challenge",
            ["components.mission-playgames-one-duels"] = "Play a Duel",
            ["components.mission-playgames-one-quickplay"] = "Play a Quick Play game",
            ["components.mission-playgames-one-maprunner"] = "Play a MapRunner game",
            ["components.mission-playgames-one-singleplayerquiz"] = "Play a Featured Quiz",
            ["components.mission-playgames-one-replayablequiz"] = "Play a Quiz",
            ["components.mission-playgames-one-classic"] = "Play a game on the {1} map",
            ["components.mission-playgames-one-anybattleroyale"] = "Play a Battle Royale game",
            ["components.mission-playgames-one-teamduels"] = "Play a Team Duel",
            ["components.mission-playgames-one-rankedduels"] = "Play a Ranked Duel",
            ["components.mission-playgames-one-rankedteamduels"] = "Play a Ranked Team Duel",
            ["components.mission-playgames-one-unrankedduels"] = "Play an Unranked Duel",
            ["components.mission-playgames-one-unrankedteamduels"] = "Play an Unranked Team Duel",

            // PlayGames — plural.
            ["components.mission-playgames-many-duels"] = "Play {0} Duels",
            ["components.mission-playgames-many-quickplay"] = "Play {0} Quick Play games",
            ["components.mission-playgames-many-maprunner"] = "Play {0} MapRunner games",
            ["components.mission-playgames-many-singleplayerquiz"] = "Play {0} Featured Quizzes",
            ["components.mission-playgames-many-replayablequiz"] = "Play {0} Quizzes",
            ["components.mission-playgames-many-classic"] = "Play {0} games on the {1} map",
            ["components.mission-playgames-many-anybattleroyale"] = "Play {0} Battle Royale games",
            ["components.mission-playgames-many-teamduels"] = "Play {0} Team Duels",
            ["components.mission-playgames-many-rankedduels"] = "Play {0} Ranked Duels",
            ["components.mission-playgames-many-rankedteamduels"] = "Play {0} Ranked Team Duels",
            ["components.mission-playgames-many-unrankedduels"] = "Play {0} Unranked Duels",
            ["components.mission-playgames-many-unrankedteamduels"] = "Play {0} Unranked Team Duels",

            // WinGames — singular.
            ["components.mission-wingames-one-duels"] = "Win a Duel",
            ["components.mission-wingames-one-maprunner"] = "Complete a MapRunner game",
            ["components.mission-wingames-one-anybattleroyale"] = "Win a Battle Royale game",
            ["components.mission-wingames-one-teamduels"] = "Win a Team Duel",
            ["components.mission-wingames-one-rankedduels"] = "Win a Ranked Duel",
            ["components.mission-wingames-one-rankedteamduels"] = "Win a Ranked Team Duel",
            ["components.mission-wingames-one-unrankedduels"] = "Win an Unranked Duel",
            ["components.mission-wingames-one-unrankedteamduels"] = "Win an Unranked Team Duel",

            // WinGames — plural.
            ["components.mission-wingames-many-duels"] = "Win {0} Duels",
            ["components.mission-wingames-many-maprunner"] = "Complete {0} MapRunner games",
            ["components.mission-wingames-many-anybattleroyale"] = "Win {0} Battle Royale games",
            ["components.mission-wingames-many-teamduels"] = "Win {0} Team Duels",
            ["components.mission-wingames-many-rankedduels"] = "Win {0} Ranked Duels",
            ["components.mission-wingames-many-rankedteamduels"] = "Win {0} Ranked Team Duels",
            ["components.mission-wingames-many-unrankedduels"] = "Win {0} Unranked Duels",
            ["components.mission-wingames-many-unrankedteamduels"] = "Win {0} Unranked Team Duels",

            // Score — plural (no singular variants exist in the source).
            ["components.mission-score-many-classic"] = "Score {0} points on the {1} map",
            ["components.mission-score-many-quickplay"] = "Score {0} points in Quick Play",
            ["components.mission-score-many-replayablequiz"] = "Score {0} points in Quizzes",
            ["components.mission-score-many-streak"] = "Make {0} correct country guesses on the {1} map",
            ["components.mission-score-many-countrystreak"] = "Make {0} correct guesses in Country Streaks",
        };
}
