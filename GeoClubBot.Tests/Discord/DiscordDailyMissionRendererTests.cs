using FluentAssertions;
using GeoClubBot.Discord.OutputAdapters;
using UseCases.OutputPorts.GeoGuessr;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class DiscordDailyMissionRendererTests
{
    private readonly DiscordDailyMissionRenderer _renderer = new();

    // ---------------------------------------------------------------------
    // "Play the Daily Challenge" — the single most common mission.
    // ---------------------------------------------------------------------

    [Fact]
    public void RenderMission_PlayDailyChallenge_Singular()
    {
        var mission = Mission(type: "PlayGames", gameMode: "DailyChallenge", target: 1);

        _renderer.RenderMission(mission).Should().Be("Play the Daily Challenge");
    }

    [Fact]
    public void RenderMission_PlayDailyChallenge_TargetGreaterThanOne_FallsBackToGenericPhrase()
    {
        // There is no "many" daily-challenge phrase, so the renderer falls back to the
        // generic PlayGames phrase using the friendly game-mode name.
        var mission = Mission(type: "PlayGames", gameMode: "DailyChallenge", target: 2);

        _renderer.RenderMission(mission).Should().Be("Play 2 Daily Challenge");
    }

    // ---------------------------------------------------------------------
    // "Play X Duels" / "Win X Duels" (+ Team variants) — high-frequency missions.
    // ---------------------------------------------------------------------

    [Theory]
    // PlayGames, Duels
    [InlineData("PlayGames", "Duels", 1, "Play a Duel")]
    [InlineData("PlayGames", "Duels", 2, "Play 2 Duels")]
    [InlineData("PlayGames", "Duels", 5, "Play 5 Duels")]
    // PlayGames, TeamDuels
    [InlineData("PlayGames", "TeamDuels", 1, "Play a Team Duel")]
    [InlineData("PlayGames", "TeamDuels", 3, "Play 3 Team Duels")]
    // WinGames, Duels
    [InlineData("WinGames", "Duels", 1, "Win a Duel")]
    [InlineData("WinGames", "Duels", 2, "Win 2 Duels")]
    [InlineData("WinGames", "Duels", 5, "Win 5 Duels")]
    // WinGames, TeamDuels
    [InlineData("WinGames", "TeamDuels", 1, "Win a Team Duel")]
    [InlineData("WinGames", "TeamDuels", 4, "Win 4 Team Duels")]
    // Ranked / Unranked variants
    [InlineData("PlayGames", "RankedDuels", 1, "Play a Ranked Duel")]
    [InlineData("PlayGames", "RankedDuels", 3, "Play 3 Ranked Duels")]
    [InlineData("WinGames", "RankedTeamDuels", 1, "Win a Ranked Team Duel")]
    [InlineData("WinGames", "RankedTeamDuels", 2, "Win 2 Ranked Team Duels")]
    [InlineData("PlayGames", "UnrankedDuels", 1, "Play an Unranked Duel")]
    [InlineData("WinGames", "UnrankedTeamDuels", 6, "Win 6 Unranked Team Duels")]
    public void RenderMission_DuelMissions(string type, string gameMode, int target, string expected)
    {
        var mission = Mission(type: type, gameMode: gameMode, target: target);

        _renderer.RenderMission(mission).Should().Be(expected);
    }

    [Fact]
    public void RenderMission_PlayManyDuels_UsesThousandsSeparator()
    {
        var mission = Mission(type: "PlayGames", gameMode: "Duels", target: 1500);

        _renderer.RenderMission(mission).Should().Be("Play 1,500 Duels");
    }

    // ---------------------------------------------------------------------
    // "Score X points on the Y Map" — high-frequency mission.
    // ---------------------------------------------------------------------

    [Fact]
    public void RenderMission_ScoreOnSpecificMap_FormatsCountAndMapName()
    {
        var mission = Mission(
            type: "Score",
            gameMode: "Classic",
            target: 25000,
            mapSlug: "famous-places",
            mapName: "Famous Places");

        _renderer.RenderMission(mission).Should().Be("Score 25,000 points on the Famous Places map");
    }

    [Fact]
    public void RenderMission_ScoreClassic_WithoutMap_UsesScoreInClassicPhrase()
    {
        // No specific map + Classic → dedicated "score X points in Classic" phrase,
        // regardless of plural.
        var mission = Mission(type: "Score", gameMode: "Classic", target: 25000, mapSlug: null);

        _renderer.RenderMission(mission).Should().Be("Score 25,000 points in Classic");
    }

    [Fact]
    public void RenderMission_ScoreClassic_WithoutMap_SingleTarget_StillUsesScoreInClassicPhrase()
    {
        var mission = Mission(type: "Score", gameMode: "Classic", target: 1, mapSlug: null);

        _renderer.RenderMission(mission).Should().Be("Score 1 points in Classic");
    }

    [Fact]
    public void RenderMission_ScoreOnSpecificMap_NullMapName_LeavesMapNameBlank()
    {
        // MapSlug present (so we keep the map-specific phrase) but MapName missing:
        // the {1} slot collapses to empty, still producing a usable sentence.
        var mission = Mission(
            type: "Score",
            gameMode: "Classic",
            target: 5000,
            mapSlug: "some-slug",
            mapName: null);

        _renderer.RenderMission(mission).Should().Be("Score 5,000 points on the  map");
    }

    [Theory]
    [InlineData("Score", "QuickPlay", 5000, null, null, "Score 5,000 points in Quick Play")]
    [InlineData("Score", "ReplayableQuiz", 3000, null, null, "Score 3,000 points in Quizzes")]
    [InlineData("Score", "CountryStreak", 10, null, null, "Make 10 correct guesses in Country Streaks")]
    public void RenderMission_OtherScoreModes(
        string type, string gameMode, int target, string? mapSlug, string? mapName, string expected)
    {
        var mission = Mission(type: type, gameMode: gameMode, target: target, mapSlug: mapSlug, mapName: mapName);

        _renderer.RenderMission(mission).Should().Be(expected);
    }

    [Fact]
    public void RenderMission_ScoreStreakOnMap_FormatsCountAndMapName()
    {
        var mission = Mission(
            type: "Score",
            gameMode: "Streak",
            target: 12,
            mapSlug: "an-arbitrary-world",
            mapName: "An Arbitrary World");

        _renderer.RenderMission(mission)
            .Should().Be("Make 12 correct country guesses on the An Arbitrary World map");
    }

    // ---------------------------------------------------------------------
    // Other PlayGames / WinGames modes and the "play on a map" phrasing.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("PlayGames", "SinglePlayerQuiz", 1, "Play a Featured Quiz")]
    [InlineData("PlayGames", "SinglePlayerQuiz", 3, "Play 3 Featured Quizzes")]
    [InlineData("PlayGames", "ReplayableQuiz", 1, "Play a Quiz")]
    [InlineData("PlayGames", "ReplayableQuiz", 4, "Play 4 Quizzes")]
    [InlineData("PlayGames", "AnyBattleRoyale", 1, "Play a Battle Royale game")]
    [InlineData("PlayGames", "AnyBattleRoyale", 2, "Play 2 Battle Royale games")]
    [InlineData("PlayGames", "QuickPlay", 1, "Play a Quick Play game")]
    [InlineData("PlayGames", "MapRunner", 1, "Play a MapRunner game")]
    [InlineData("WinGames", "MapRunner", 1, "Complete a MapRunner game")]
    [InlineData("WinGames", "MapRunner", 3, "Complete 3 MapRunner games")]
    [InlineData("WinGames", "AnyBattleRoyale", 1, "Win a Battle Royale game")]
    public void RenderMission_OtherPlayAndWinModes(string type, string gameMode, int target, string expected)
    {
        var mission = Mission(type: type, gameMode: gameMode, target: target);

        _renderer.RenderMission(mission).Should().Be(expected);
    }

    [Fact]
    public void RenderMission_PlayOnSpecificClassicMap_Singular()
    {
        var mission = Mission(
            type: "PlayGames",
            gameMode: "Classic",
            target: 1,
            mapSlug: "a-community-map",
            mapName: "A Community Map");

        _renderer.RenderMission(mission).Should().Be("Play a game on the A Community Map map");
    }

    [Fact]
    public void RenderMission_PlayOnSpecificClassicMap_Plural()
    {
        var mission = Mission(
            type: "PlayGames",
            gameMode: "Classic",
            target: 3,
            mapSlug: "a-community-map",
            mapName: "A Community Map");

        _renderer.RenderMission(mission).Should().Be("Play 3 games on the A Community Map map");
    }

    // ---------------------------------------------------------------------
    // Plural-boundary behaviour.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(0)] // 0 is not > 1 → treated as singular
    [InlineData(1)]
    public void RenderMission_TargetAtOrBelowOne_UsesSingularPhrase(int target)
    {
        var mission = Mission(type: "PlayGames", gameMode: "Duels", target: target);

        _renderer.RenderMission(mission).Should().Be("Play a Duel");
    }

    // ---------------------------------------------------------------------
    // Invalid / unexpected data — the renderer must still produce something.
    // ---------------------------------------------------------------------

    [Fact]
    public void RenderMission_NullMission_Throws()
    {
        var act = () => _renderer.RenderMission(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderMission_UnknownType_NoFallback_ReturnsRawSpecificKey()
    {
        // "Unknown" maps to a null generic key, so the raw, lower-cased specific key
        // is surfaced rather than throwing.
        var mission = Mission(type: "Unknown", gameMode: "Duels", target: 1);

        _renderer.RenderMission(mission).Should().Be("components.mission-unknown-one-duels");
    }

    [Fact]
    public void RenderMission_UnrecognisedType_ReturnsRawSpecificKey()
    {
        var mission = Mission(type: "Teleport", gameMode: "Duels", target: 2);

        _renderer.RenderMission(mission).Should().Be("components.mission-teleport-many-duels");
    }

    [Fact]
    public void RenderMission_KnownTypeUnknownMode_UsesGenericPhraseWithRawModeName()
    {
        // PlayGames is a known type, but "Hyperspace" has no specific phrase and no friendly
        // name, so the generic phrase falls back to the raw game-mode string.
        var mission = Mission(type: "PlayGames", gameMode: "Hyperspace", target: 3);

        _renderer.RenderMission(mission).Should().Be("Play 3 Hyperspace");
    }

    [Fact]
    public void RenderMission_KnownTypeKnownModeWithoutSpecificPhrase_UsesFriendlyModeName()
    {
        // WinGames + Classic has no specific phrase; generic fallback uses the friendly name.
        var mission = Mission(type: "WinGames", gameMode: "Classic", target: 2, mapSlug: "x", mapName: "X");

        _renderer.RenderMission(mission).Should().Be("Win 2 Classic");
    }

    [Fact]
    public void RenderMission_GenericFallback_DoesNotApplyThousandsSeparator()
    {
        // The generic fallback formats the count with the invariant culture (no separators),
        // unlike the specific phrases which use thousands separators.
        var mission = Mission(type: "PlayGames", gameMode: "Hyperspace", target: 12000);

        _renderer.RenderMission(mission).Should().Be("Play 12000 Hyperspace");
    }

    [Fact]
    public void RenderMission_GameModeNoneWithKnownType_UsesRawModeName()
    {
        // "None" has an explicit null friendly name, so it falls back to the raw value.
        var mission = Mission(type: "WinGames", gameMode: "None", target: 2);

        _renderer.RenderMission(mission).Should().Be("Win 2 None");
    }

    [Fact]
    public void RenderMission_IsCaseInsensitiveForSpecificKeyLookup()
    {
        // The specific key is lower-cased before lookup, so mixed-case modes still resolve.
        var mission = Mission(type: "PlayGames", gameMode: "DUELS", target: 1);

        _renderer.RenderMission(mission).Should().Be("Play a Duel");
    }

    /// <summary>
    /// Builds a <see cref="DailyMissionDto"/> with sensible defaults for the many
    /// required fields the renderer ignores, so tests only specify what matters.
    /// </summary>
    private static DailyMissionDto Mission(
        string type,
        string gameMode,
        int target,
        string? mapSlug = null,
        string? mapName = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            GameMode = gameMode,
            CurrentProgress = 0,
            TargetProgress = target,
            Completed = false,
            EndDate = DateTimeOffset.UnixEpoch,
            RewardAmount = 0,
            RewardType = "Xp",
            MapSlug = mapSlug,
            MapName = mapName,
        };
}
