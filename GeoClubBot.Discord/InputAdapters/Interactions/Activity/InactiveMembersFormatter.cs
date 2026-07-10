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

    // Discord caps an embed description at 4096 characters; stop well short and summarise the rest so
    // a very large club never trips the API limit.
    private const int MaxListedMembers = 60;

    public static EmbedBuilder BuildEmbed(TodaysInactiveMembers report)
    {
        var day = report.Day.ToString("yyyy-MM-dd", Invariant);
        var title = $"😴 Today's Inactive Members — {report.ClubName}";

        if (report.Members.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithColor(AllActiveColor)
                .WithDescription($"🎉 Everyone completed their daily mission today ({day}).")
                .WithFooter($"{report.TotalMembers.ToString(Invariant)} member(s) checked");
        }

        var description = new StringBuilder()
            .AppendLine(
                $"**{report.Members.Count.ToString(Invariant)}** of **{report.TotalMembers.ToString(Invariant)}** member(s) "
                + $"have not done today's daily mission ({day}):")
            .AppendLine();

        foreach (var member in report.Members.Take(MaxListedMembers))
        {
            description.AppendLine(FormatMember(member));
        }

        if (report.Members.Count > MaxListedMembers)
        {
            description.AppendLine($"…and {(report.Members.Count - MaxListedMembers).ToString(Invariant)} more.");
        }

        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(InactiveColor)
            .WithDescription(description.ToString())
            .WithFooter($"{report.TotalMembers.ToString(Invariant)} member(s) checked · linked members are shown with their Discord handle");
    }

    // GeoGuessr nick first (always present), plus a Discord mention when the account is linked. The
    // mention only renders as a name here: the embed is delivered ephemerally with mentions
    // suppressed, so a listed member is never notified that they were reported.
    private static string FormatMember(InactiveMember member) =>
        member.DiscordUserId is { } discordUserId
            ? $"• {member.Nickname} (<@{discordUserId.ToString(Invariant)}>)"
            : $"• {member.Nickname}";
}
