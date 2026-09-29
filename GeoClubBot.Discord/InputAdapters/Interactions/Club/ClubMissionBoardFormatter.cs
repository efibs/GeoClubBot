using System.Globalization;
using System.Text;
using Discord;
using Entities;
using UseCases.UseCases.MissionBoard;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Club;

/// <summary>Renders <c>/club-stats board</c>: where the club stands on this week's mission boards.</summary>
internal static class ClubMissionBoardFormatter
{
    private static readonly Color BoardColor = new(0x34, 0x98, 0xDB);
    private static readonly Color AllClearedColor = new(0x2E, 0xCC, 0x71);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // An embed field holds at most 1024 characters; a full 5×5 board can have many open claims.
    private const int MaxListedOpenMissions = 10;
    private const int ProgressBarLength = 10;

    public static EmbedBuilder BuildEmbed(ClubMissionBoardView view)
    {
        var board = view.Board;

        var embed = new EmbedBuilder()
            .WithTitle($"🗺️ Mission Board — {view.ClubName}")
            .WithColor(board.AllBoardsCleared ? AllClearedColor : BoardColor)
            .WithDescription(
                $"Board week ends {Timestamp(board.PeriodEnd)} · daily claims reset {Timestamp(view.ClaimCycleStart.AddDays(1))}")
            .AddField("Boards", BuildBoardsValue(board));

        var footer = $"{board.CompletedTileCount.ToString(Invariant)} / {board.TotalTileCount.ToString(Invariant)} missions completed this week";
        if (!board.AllBoardsCleared)
        {
            embed.AddField("Open missions", BuildOpenMissionsValue(view));
            footer += " · 🆘 help requested";
        }

        return embed.WithFooter(footer);
    }

    private static string BuildBoardsValue(ClubMissionBoardWeek board)
    {
        var builder = new StringBuilder();

        foreach (var b in board.Boards)
        {
            var name = $"Board {b.Number.ToString(Invariant)} ({b.Size.ToString(Invariant)}×{b.Size.ToString(Invariant)})";

            if (b.IsCleared)
            {
                builder.Append("✅ ").Append(name).Append(" cleared");
                if (b.ClearedAt is { } clearedAt)
                {
                    builder.Append(' ').Append(Timestamp(clearedAt));
                }
            }
            else if (board.CurrentBoard?.Number == b.Number)
            {
                var free = b.Tiles.Count(t => t.IsFree);
                builder.Append("▶️ **").Append(name).Append("** ")
                    .Append(ProgressBar(b.CompletedCount, b.Tiles.Count)).Append(' ')
                    .Append(b.CompletedCount.ToString(Invariant)).Append('/').Append(b.Tiles.Count.ToString(Invariant))
                    .Append(" done · ").Append(free.ToString(Invariant)).Append(" free");
            }
            else
            {
                builder.Append("🔒 ").Append(name);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildOpenMissionsValue(ClubMissionBoardView view)
    {
        var open = view.Board.OpenClaims
            .OrderBy(t => t.ClaimedAt)
            .ToList();

        if (open.Count == 0)
        {
            return "None — every claimed mission is done.";
        }

        var builder = new StringBuilder();
        foreach (var tile in open.Take(MaxListedOpenMissions))
        {
            builder.Append("• **").Append(view.NicknameOf(tile.ClaimedBy!)).Append("** — ").Append(tile.Title)
                .Append(" (").Append(tile.CurrentProgress.ToString(Invariant)).Append('/')
                .Append(tile.TargetProgress.ToString(Invariant)).Append(')');

            if (tile.ClaimedAt is { } claimedAt)
            {
                builder.Append(" · claimed ").Append(Timestamp(claimedAt));
            }

            if (tile.HelpRequested)
            {
                builder.Append(" 🆘");
            }

            builder.AppendLine();
        }

        if (open.Count > MaxListedOpenMissions)
        {
            builder.Append("…and ").Append((open.Count - MaxListedOpenMissions).ToString(Invariant)).Append(" more.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string ProgressBar(int done, int total)
    {
        var filled = total == 0 ? 0 : (int)Math.Round((double)done / total * ProgressBarLength);
        return new string('▰', filled) + new string('▱', ProgressBarLength - filled);
    }

    private static string Timestamp(DateTimeOffset at) =>
        $"<t:{at.ToUnixTimeSeconds().ToString(Invariant)}:R>";
}
