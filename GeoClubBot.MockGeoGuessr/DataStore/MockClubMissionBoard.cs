using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.MockGeoGuessr.DataStore;

/// <summary>
/// A stand-in for one club's weekly mission board: five boards (3×3, 4×4 and three 5×5) unlocked
/// one after the other. Completing a mission credits a type-5 feed entry to its claimer; clearing a
/// board credits a type-6 bonus to whoever completed the last mission — as on the live API.
/// </summary>
public sealed class MockClubMissionBoard
{
    private static readonly int[] BoardSizes = [3, 4, 5, 5, 5];

    // Real templates observed on the live API (Tools/GeoClubBot.ApiProbe `board`).
    private static readonly (ClubMissionTemplateDto Template, string Type, string GameMode, int Target, int Threshold)[] Templates =
    [
        (Template("ranked-duel-wins", "Win a Ranked Duel", "Win {0} Ranked Duels"), "WinGames", "RankedDuels", 2, 0),
        (Template("ranked-team-duels-wins", "Win a ranked Team Duel", "Win {0} ranked Team Duels"), "WinGames", "RankedTeamDuels", 1, 0),
        (Template("classic-good-guess", "Score 4500 or more on a guess on {1}", "Score 4500 or more on {0} guesses on {1}", "World"), "HighScoreGuesses", "Classic", 4, 4500),
        (Template("score-in-classic", "Score {0} point on a Classic Map", "Score {0} points on a Classic Map"), "Score", "Classic", 30000, 0),
        (Template("play-ranked-duels", "Play {0} Ranked Duel", "Play {0} Ranked Duels"), "PlayGames", "RankedDuels", 3, 0),
        (Template("ranked-duels-damage", "Deal damage in ranked Duels", "Deal {0} damage in ranked Duels"), "DealDamage", "RankedDuels", 10000, 0),
        (Template("visit-continents-ranked-duels", "Visit one continent in Ranked Duels", "Visit {0} continents in Ranked Duels"), "VisitContinents", "RankedDuels", 5, 0),
        (Template("classic-5k-guess", "Do a 5K on {1}", "Do {0} 5K's on {1}", "World"), "HighScoreGuesses", "Classic", 2, 5000),
        (Template("win-rounds-duels", "Win {0} round in a Ranked Duel", "Win {0} rounds in Ranked Duels"), "WinRounds", "RankedDuels", 10, 0)
    ];

    private readonly Lock _lock = new();
    private ClubMissionBoardSnapshotDto _current;
    private ClubMissionBoardSnapshotDto? _previous;

    public MockClubMissionBoard(DateTimeOffset periodStart)
    {
        _current = Generate(periodStart);
    }

    /// <summary>A copy of the running board week, as <c>GET /v4/missions/club/board</c> returns it.</summary>
    public ClubMissionBoardSnapshotDto Current
    {
        get
        {
            lock (_lock)
            {
                return Clone(_current);
            }
        }
    }

    /// <summary>A copy of the previous board week; null before the first reset.</summary>
    public ClubMissionBoardSnapshotDto? Previous
    {
        get
        {
            lock (_lock)
            {
                return _previous is null ? null : Clone(_previous);
            }
        }
    }

    /// <summary>Starts a new board week; the running one becomes the previous one.</summary>
    public void Reset(DateTimeOffset periodStart)
    {
        lock (_lock)
        {
            _previous = _current;
            _current = Generate(periodStart);
        }
    }

    /// <summary>Claims a free mission of the current board for <paramref name="userId"/>.</summary>
    public ClubMissionTileDto Claim(string userId, Guid? missionId = null)
    {
        lock (_lock)
        {
            var board = CurrentBoard() ?? throw new InvalidOperationException("Every board is cleared.");
            if (_current.Boards.SelectMany(b => b.Tiles).Any(t => t.ClaimedBy == userId && !t.Completed))
            {
                throw new InvalidOperationException($"{userId} already holds an open mission.");
            }

            var tile = missionId is { } id
                ? board.Tiles.FirstOrDefault(t => t.MissionId == id)
                  ?? throw new InvalidOperationException($"Mission {id} is not on the current board.")
                : board.Tiles.FirstOrDefault(t => t.ClaimedBy is null)
                  ?? throw new InvalidOperationException("No mission is free on the current board.");

            if (tile.ClaimedBy is not null)
            {
                throw new InvalidOperationException($"Mission {tile.MissionId} is already claimed.");
            }

            tile.ClaimedBy = userId;
            tile.ClaimedAt = DateTimeOffset.UtcNow;
            return tile;
        }
    }

    public void RequestHelp(Guid missionId)
    {
        lock (_lock)
        {
            Find(missionId).HelpRequestedAt = DateTimeOffset.UtcNow;
        }
    }

    public void AddHelper(Guid missionId, string userId)
    {
        lock (_lock)
        {
            var tile = Find(missionId);
            if (!tile.Helpers.Contains(userId))
            {
                tile.Helpers.Add(userId);
            }
        }
    }

    /// <summary>Moves a mission's claim and help request back in time, e.g. to make it look stuck.</summary>
    public void Backdate(Guid missionId, TimeSpan by)
    {
        lock (_lock)
        {
            var tile = Find(missionId);
            tile.ClaimedAt -= by;
            tile.HelpRequestedAt -= by;
        }
    }

    public void SetProgress(Guid missionId, int progress)
    {
        lock (_lock)
        {
            var tile = Find(missionId);
            tile.CurrentProgress = Math.Clamp(progress, 0, tile.TargetProgress);
        }
    }

    /// <summary>
    /// Completes a claimed mission. Returns the feed entries GeoGuessr would record: the mission's XP
    /// for the claimer and, when that cleared the board, the board bonus for them too.
    /// </summary>
    public IReadOnlyList<(string UserId, int Xp, int Type)> Complete(Guid missionId)
    {
        lock (_lock)
        {
            var tile = Find(missionId);
            if (tile.ClaimedBy is not { } claimer)
            {
                throw new InvalidOperationException($"Mission {missionId} is not claimed.");
            }

            if (tile.Completed)
            {
                return [];
            }

            var now = DateTimeOffset.UtcNow;
            tile.CurrentProgress = tile.TargetProgress;
            tile.Completed = true;
            tile.CompletedAt = now;

            var entries = new List<(string, int, int)> { (claimer, tile.RewardXp, 5) };

            var board = _current.Boards.First(b => b.Tiles.Contains(tile));
            if (board.Tiles.All(t => t.Completed))
            {
                board.Status = "Cleared";
                board.ClearedAt = now;
                entries.Add((claimer, board.ClearRewardXp, 6));

                _current.CurrentBoardNumber = board.Number + 1;
                var next = _current.Boards.FirstOrDefault(b => b.Number == board.Number + 1);
                if (next is null)
                {
                    _current.AllBoardsCleared = true;
                }
                else
                {
                    next.Status = "Active";
                }
            }

            return entries;
        }
    }

    private ClubMissionBoardDto? CurrentBoard() =>
        _current.AllBoardsCleared ? null : _current.Boards.FirstOrDefault(b => b.Number == _current.CurrentBoardNumber);

    private ClubMissionTileDto Find(Guid missionId) =>
        _current.Boards.SelectMany(b => b.Tiles).FirstOrDefault(t => t.MissionId == missionId)
        ?? throw new InvalidOperationException($"Mission {missionId} is not on the board.");

    private static ClubMissionBoardSnapshotDto Generate(DateTimeOffset periodStart)
    {
        var boards = BoardSizes
            .Select((size, i) => new ClubMissionBoardDto
            {
                Number = i + 1,
                Size = size,
                ClearRewardXp = 100,
                Status = i == 0 ? "Active" : "Locked",
                Tiles = Enumerable.Range(0, size * size)
                    .Select(index =>
                    {
                        var (template, type, gameMode, target, threshold) = Templates[Random.Shared.Next(Templates.Length)];
                        return new ClubMissionTileDto
                        {
                            MissionId = Guid.NewGuid(),
                            TemplateId = template.Id,
                            Index = index,
                            Type = type,
                            GameMode = gameMode,
                            TargetProgress = target,
                            Threshold = threshold,
                            RewardXp = 20
                        };
                    })
                    .ToList()
            })
            .ToList();

        return new ClubMissionBoardSnapshotDto
        {
            PeriodKey = periodStart.ToString("O"),
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddDays(7),
            CurrentBoardNumber = 1,
            AllBoardsCleared = false,
            Boards = boards,
            Templates = Templates.Select(t => t.Template).DistinctBy(t => t.Id).ToDictionary(t => t.Id),
            You = new ClubMissionBoardYouDto { NextDayAt = NextDailyReset(DateTimeOffset.UtcNow, periodStart) }
        };
    }

    /// <summary>The claim day flips at the same time of day as the board week.</summary>
    private static DateTimeOffset NextDailyReset(DateTimeOffset now, DateTimeOffset periodStart)
    {
        var next = periodStart;
        while (next <= now)
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static ClubMissionTemplateDto Template(string id, string title, string titlePlural, string? mapName = null) =>
        new() { Id = id, Title = title, TitlePlural = titlePlural, MapName = mapName };

    // A deep copy, so callers never see (or change) the live state mid-update.
    private static ClubMissionBoardSnapshotDto Clone(ClubMissionBoardSnapshotDto source)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<ClubMissionBoardSnapshotDto>(
            System.Text.Json.JsonSerializer.Serialize(source))!;
        copy.You ??= new ClubMissionBoardYouDto();
        copy.You.NextDayAt = NextDailyReset(DateTimeOffset.UtcNow, copy.PeriodStart);
        return copy;
    }
}
