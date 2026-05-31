using System.Text;
using Entities;
using UseCases.OutputPorts.Notifications;

namespace GeoClubBot.Discord.OutputAdapters;

/// <summary>
/// Discord-flavoured rendering of activity-status messages — ANSI colour codes in code
/// blocks for the out-of-strikes warning, plain Markdown bullet lists for the rest.
/// </summary>
public sealed class DiscordActivityStatusMessageFormatter : IActivityStatusMessageFormatter
{
    public string FormatStatusUpdateHeader(IReadOnlyList<ClubMemberActivityStatus> firstChunk, string clubName, int minXP)
    {
        var builder = new StringBuilder($"**======= Activity status update - {clubName} =======**\n\n");

        builder.Append("Members that failed to meet the ");
        builder.Append(minXP);
        builder.Append("XP requirement:");

        if (firstChunk.Count == 0)
        {
            builder.Append("\n```ansi\n\e[2;31m\e[0m\e[2;32mNone :)\e[0m\n```");
        }

        AppendPlayers(builder, firstChunk);

        return builder.ToString();
    }

    public string FormatPlayerChunk(IReadOnlyList<ClubMemberActivityStatus> players)
    {
        var builder = new StringBuilder();
        AppendPlayers(builder, players);
        return builder.ToString();
    }

    public string FormatIndividualTargets(IReadOnlyList<ClubMemberActivityStatus> playersWithIndividualTarget)
    {
        var builder = new StringBuilder("​\nPlayers that have an individual target:");

        foreach (var player in playersWithIndividualTarget)
        {
            builder.AppendLine();
            builder.Append("* ");
            builder.Append(player.Nickname);
            builder.Append(" - individual target: ");
            builder.Append(player.IndividualTarget);
            builder.Append("XP; Reason(s): ");
            builder.Append(player.IndividualTargetReason);
        }

        return builder.ToString();
    }

    public string? FormatAverageXpSummary(
        IReadOnlyList<ClubMemberAverageXp> topMembers,
        IReadOnlyList<ClubMemberAverageXp> bottomMembers,
        int historyDepth)
    {
        var builder = new StringBuilder();

        if (topMembers.Count > 0)
        {
            builder.Append($"​\nTop {topMembers.Count} members by average XP (last {historyDepth} intervals):");
            for (var i = 0; i < topMembers.Count; i++)
            {
                builder.AppendLine();
                builder.Append($"{i + 1}. {topMembers[i].Nickname} — {topMembers[i].AverageXp:F1}XP");
            }
        }

        if (bottomMembers.Count > 0)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append($"​\nBottom {bottomMembers.Count} members by average XP (last {historyDepth} intervals):");
            for (var i = 0; i < bottomMembers.Count; i++)
            {
                builder.AppendLine();
                builder.Append($"{i + 1}. {bottomMembers[i].Nickname} — {bottomMembers[i].AverageXp:F1}XP");
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static void AppendPlayers(StringBuilder builder, IReadOnlyList<ClubMemberActivityStatus> players)
    {
        foreach (var player in players)
        {
            builder.AppendLine();

            if (player.IsOutOfStrikes)
            {
                builder.Append("```ansi\n\e[2;31m");
                builder.Append(player.Nickname);
                builder.Append("\e[0m got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP");
                if (player.IndividualTargetReason != null)
                {
                    builder.Append(" (individual target: ");
                    builder.Append(player.IndividualTarget);
                    builder.Append("XP - ");
                    builder.Append(player.IndividualTargetReason);
                    builder.Append(")");
                }
                builder.Append(" and already had ");
                builder.Append(player.NumStrikes - 1);
                builder.Append(" strikes and therefore \e[2;31mneeds to be kicked\e[0m.\n```");
            }
            else
            {
                builder.Append("* ");
                builder.Append(player.Nickname);
                builder.Append(" got only ");
                builder.Append(player.XpSinceLastUpdate);
                builder.Append("XP");
                if (player.IndividualTargetReason != null)
                {
                    builder.Append(" (individual target: ");
                    builder.Append(player.IndividualTarget);
                    builder.Append("XP - ");
                    builder.Append(player.IndividualTargetReason);
                    builder.Append(")");
                }
                builder.Append(" and therefore is now on ");
                builder.Append(player.NumStrikes);
                builder.Append(" strikes.");
            }
        }
    }
}
