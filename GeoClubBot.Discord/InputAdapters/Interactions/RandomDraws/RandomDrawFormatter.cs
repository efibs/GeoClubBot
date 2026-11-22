using Discord;
using UseCases.UseCases.RandomDraws;

namespace GeoClubBot.Discord.InputAdapters.Interactions.RandomDraws;

/// <summary>A team of <c>/random teams</c>: its members, already rendered.</summary>
public sealed record DrawTeam(string Name, IReadOnlyList<string> Members);

/// <summary>
/// What one draw shows: a title, the items drawn (rendered, in the order drawn), an optional line under
/// them, and a footer saying what was drawn from and by whom, so everyone sees the pool.
/// </summary>
public sealed record DrawCard(
    string Title,
    IReadOnlyList<string> Items,
    string Footer,
    string? Note = null,
    IReadOnlyList<DrawTeam>? Teams = null);

/// <summary>Renders the results of <c>/random</c>. Pure; mentions in it are shown, never pinged.</summary>
public static class RandomDrawFormatter
{
    public static readonly Color DrawColor = new(0x5865F2);

    private const int MaxDescriptionLength = 4096;
    private const int MaxFieldValueLength = 1024;
    private const int MaxEmbedLength = 6000;

    /// <summary>The card as an embed, or null when it holds more than one Discord embed can show.</summary>
    public static Embed? Render(DrawCard card)
    {
        var description = Description(card);
        var fields = (card.Teams ?? [])
            .Select(team => (Name: $"{team.Name} ({team.Members.Count})",
                Value: team.Members.Count == 0 ? "*nobody*" : string.Join("\n", team.Members)))
            .ToList();

        var length = card.Title.Length + description.Length + card.Footer.Length
            + fields.Sum(field => field.Name.Length + field.Value.Length);
        if (description.Length > MaxDescriptionLength
            || fields.Exists(field => field.Value.Length > MaxFieldValueLength)
            || length > MaxEmbedLength)
        {
            return null;
        }

        var builder = new EmbedBuilder()
            .WithTitle(card.Title)
            .WithColor(DrawColor)
            .WithFooter(card.Footer);

        if (description.Length > 0)
        {
            builder.WithDescription(description);
        }

        foreach (var (name, value) in fields)
        {
            builder.AddField(name, value, inline: true);
        }

        return builder.Build();
    }

    private static string Description(DrawCard card)
    {
        var lines = card.Items.Count == 1
            ? [$"## {card.Items[0]}"]
            : card.Items.Select((item, index) => $"{index + 1}. {item}").ToList();

        if (card.Note is { } note)
        {
            lines.Add(lines.Count == 0 ? note : $"\n{note}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>"🇫🇷 [France](https://www.plonkit.net/france)", the link only when PlonkIt has a guide.</summary>
    public static string Country(DrawableCountry country)
    {
        var flag = country.Flag.Length > 0 ? $"{country.Flag} " : string.Empty;
        return country.PlonkItUrl is { } url ? $"{flag}[{country.Name}]({url})" : $"{flag}{country.Name}";
    }

    /// <summary>The GeoGuessr nickname linked to the profile, the Discord account when linked, and the club when several were drawn from.</summary>
    public static string Member(DrawableClubMember member, bool withClub)
    {
        var text = $"[{Escape(member.Nickname)}](https://www.geoguessr.com/user/{Uri.EscapeDataString(member.UserId)})";
        if (member.DiscordUserId is { } discordUserId)
        {
            text += $" · {Person(discordUserId)}";
        }

        return withClub ? $"{text} · {Escape(member.ClubName)}" : text;
    }

    public static string Person(ulong discordUserId) => $"<@{discordUserId}>";

    /// <summary>"3 of 41 · 🔊 General · drawn by Fibs".</summary>
    public static string Footer(int drawn, int poolSize, string pool, string caller) =>
        $"{drawn} of {poolSize} · {pool} · drawn by {caller}";

    /// <summary>Said under a draw that wanted more than the pool held.</summary>
    public static string? ShortPoolNote(int wanted, int poolSize) =>
        wanted > poolSize ? $"-# Only {poolSize} to draw from, so everyone was drawn." : null;

    public static string Escape(string text) => Format.Sanitize(text);
}
