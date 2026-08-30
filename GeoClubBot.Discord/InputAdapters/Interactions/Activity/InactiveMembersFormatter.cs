using System.Globalization;
using System.Text;
using Discord;
using UseCases.UseCases.InactiveMembers;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

internal static class InactiveMembersFormatter
{
    private static readonly Color InactiveColor = new(0xE7, 0x4C, 0x3C);
    private static readonly Color AllActiveColor = new(0x2E, 0xCC, 0x71);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Discord caps an embed description at 4096 characters. Two lists share that budget, so each
    // one stops well short and summarises the rest - a very large club never trips the API limit.
    private const int MaxListedMembers = 30;

    public static EmbedBuilder BuildEmbed(TodaysInactiveMembers report)
    {
        var day = report.Day.ToString("yyyy-MM-dd", Invariant);
        var title = $"😴 Today's Inactive Members — {report.ClubName}";
        var footer = $"{report.TotalMembers.ToString(Invariant)} member(s) checked";

        if (report.MissionInactive.Count == 0 && report.ChallengeInactive.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithColor(AllActiveColor)
                .WithDescription($"🎉 Everyone earned both of today's club XP awards ({day}).")
                .WithFooter(footer);
        }

        // Two independent awards, so two independent lists: a member missing from one may well
        // appear in the other, and someone who did neither is on both.
        var description = new StringBuilder()
            .Append(BuildSection("🎯 Haven't done the daily mission", report.MissionInactive, report.TotalMembers, day))
            .AppendLine()
            .Append(BuildSection("🌍 Haven't played the daily challenge (or won a duel)", report.ChallengeInactive, report.TotalMembers, day));

        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(InactiveColor)
            .WithDescription(description.ToString())
            .WithFooter($"{footer} · linked members are shown with their Discord handle");
    }

    private static string BuildSection(
        string heading,
        IReadOnlyList<InactiveMember> members,
        int totalMembers,
        string day)
    {
        var section = new StringBuilder().AppendLine($"**{heading}**");

        if (members.Count == 0)
        {
            return section.AppendLine($"🎉 Everyone did ({day}).").ToString();
        }

        section.AppendLine(
            $"{members.Count.ToString(Invariant)} of {totalMembers.ToString(Invariant)} member(s) ({day}):");

        foreach (var member in members.Take(MaxListedMembers))
        {
            section.AppendLine(FormatMember(member));
        }

        if (members.Count > MaxListedMembers)
        {
            section.AppendLine($"…and {(members.Count - MaxListedMembers).ToString(Invariant)} more.");
        }

        return section.ToString();
    }

    // GeoGuessr nick first (always present), plus a Discord mention when the account is linked. The
    // mention only renders as a name here: the embed is delivered ephemerally with mentions
    // suppressed, so a listed member is never notified that they were reported.
    private static string FormatMember(InactiveMember member) =>
        member.DiscordUserId is { } discordUserId
            ? $"• {member.Nickname} (<@{discordUserId.ToString(Invariant)}>)"
            : $"• {member.Nickname}";
}
