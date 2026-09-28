using System.Text;
using Entities;
using Extensions;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges.Configuration;

namespace UseCases.UseCases.CountryChallenges.Rendering;

/// <summary>A message ready to send, with the pings it is allowed to make.</summary>
public sealed record RenderedMessage(string Content, MessageMentions Mentions);

/// <summary>One challenge of a day's announcement. <see cref="Link"/> is null when GeoGuessr refused to create it.</summary>
public sealed record AnnouncementItem(ChallengePlan Challenge, CountryPlan Country, string? Link);

/// <summary>The announcement for one channel, and the name of its thread when one should be opened.</summary>
public sealed record ChannelAnnouncement(
    ulong ChannelId,
    RenderedMessage Message,
    string? ThreadName,
    IReadOnlyList<AnnouncementItem> Items);

/// <summary>
/// A challenge whose results were evaluated: its post, the settings it was evaluated with, its players
/// best first, and the points each of them earned.
/// </summary>
public sealed record EvaluatedChallenge(
    CountryChallengePost Post,
    ChallengeResultsPlan Results,
    IReadOnlyList<ClubChallengeResultPlayer> Players,
    IReadOnlyList<int> Points);

public sealed record ChannelResults(ulong ChannelId, RenderedMessage Message, IReadOnlyList<EvaluatedChallenge> Challenges);

/// <summary>
/// Builds every country challenge message. The run and the preview both go through here, which is what
/// makes the preview show exactly what would be posted.
/// </summary>
public static class CountryChallengeMessages
{
    /// <summary>The link a preview shows in place of a challenge that has not been created.</summary>
    public const string PreviewLink = "https://www.geoguessr.com/challenge/PREVIEW";

    private const int ThreadNameMaxLength = 100;

    /// <summary>One message per channel with all of the day's challenges posted there.</summary>
    public static IReadOnlyList<ChannelAnnouncement> Announcements(
        CountryChallengePlan plan,
        DateOnly date,
        IReadOnlyList<AnnouncementItem> items)
    {
        return items
            .GroupBy(i => i.Challenge.ChannelId)
            .Select(group => Announcement(plan, date, group.Key, group.ToList()))
            .ToList();
    }

    /// <summary>One message per channel with the results of every challenge evaluated in a run.</summary>
    public static IReadOnlyList<ChannelResults> Results(
        CountryChallengePlan plan,
        DateOnly date,
        IReadOnlyList<EvaluatedChallenge> evaluated)
    {
        return evaluated
            .Where(e => e.Results.Post)
            .GroupBy(e => e.Results.ChannelId ?? e.Post.ChannelId)
            .Select(group =>
            {
                var challenges = group.ToList();
                var values = Values(
                    ("mentions", Mentions(plan.Results.MentionRoleIds)),
                    ("day", Day(date)),
                    ("results", string.Join("\n", challenges.Select(ResultsEntry))));

                var content = CountryChallengeTemplate.Render(plan.Results.Message, values, date).Trim();
                var mentions = CountryChallengeTemplate.ExtractMentions(
                    [plan.Results.Message, .. challenges.Select(c => c.Results.Entry)],
                    plan.Results.MentionRoleIds);

                return new ChannelResults(group.Key, new RenderedMessage(content, mentions), challenges);
            })
            .ToList();
    }

    public static RenderedMessage Leaderboard(
        CountryChallengePlan plan,
        DateOnly date,
        IReadOnlyList<CountryChallengeStanding> standings)
    {
        var lines = standings
            .Where(s => s.Rank <= plan.Leaderboard.Top)
            .Select(LeaderboardLine)
            .ToList();

        var values = Values(
            ("mentions", Mentions(plan.Leaderboard.MentionRoleIds)),
            ("season", plan.Leaderboard.Season),
            ("day", Day(date)),
            ("leaderboard", lines.Count == 0 ? "No points yet." : string.Join("\n", lines)));

        var content = CountryChallengeTemplate.Render(plan.Leaderboard.Message, values, date).Trim();
        var mentions = CountryChallengeTemplate.ExtractMentions([plan.Leaderboard.Message], plan.Leaderboard.MentionRoleIds);

        return new RenderedMessage(content, mentions);
    }

    public static string LeaderboardLine(CountryChallengeStanding standing) =>
        $"{Medal(standing.Rank)} {EscapeMarkdown(standing.Nickname)} · {standing.Points} {(standing.Points == 1 ? "pt" : "pts")}";

    public static string ChallengeLink(string challengeId) => $"https://www.geoguessr.com/challenge/{challengeId}";

    public static string MapLink(string mapId) => $"https://www.geoguessr.com/maps/{mapId}";

    public static string Medal(int place) => place switch
    {
        1 => ":first_place:",
        2 => ":second_place:",
        3 => ":third_place:",
        _ => $"{place}."
    };

    /// <summary>"A", "A & B", "A, B & C".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} & {names[^1]}"
    };

    /// <summary>
    /// Escapes the characters Discord would read as formatting. Only for text that comes from outside —
    /// GeoGuessr nicknames — never for the file's own values, whose formatting is intended.
    /// </summary>
    public static string EscapeMarkdown(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '<' or '[' or ']')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static ChannelAnnouncement Announcement(
        CountryChallengePlan plan,
        DateOnly date,
        ulong channelId,
        IReadOnlyList<AnnouncementItem> items)
    {
        var created = items.Where(i => i.Link is not null).ToList();
        var mentionRoleIds = created.SelectMany(i => i.Challenge.MentionRoleIds).Distinct().ToList();

        var values = Values(
            ("mentions", Mentions(mentionRoleIds)),
            ("day", Day(date)),
            ("names", JoinNames(DistinctNames(items))),
            ("challenges", string.Join("\n", items.Select(i => AnnouncementEntry(i, date)))));

        var content = CountryChallengeTemplate.Render(plan.Announcement.Message, values, date).Trim();

        // A message that only reports challenges that could not be created pings no one.
        var mentions = created.Count == 0
            ? MessageMentions.None
            : CountryChallengeTemplate.ExtractMentions(
                [plan.Announcement.Message, .. created.Select(i => i.Challenge.Entry)],
                mentionRoleIds);

        var threadName = plan.Announcement.Thread.Enabled && created.Count > 0
            ? ThreadName(plan, date, created)
            : null;

        return new ChannelAnnouncement(channelId, new RenderedMessage(content, mentions), threadName, items);
    }

    private static string AnnouncementEntry(AnnouncementItem item, DateOnly date)
    {
        if (item.Link is null)
        {
            return $":warning: **{item.Challenge.Name}** ({item.Country.Name}) could not be created today.";
        }

        var country = item.Country;
        var values = EntryValues(
            item.Challenge.Name, country.Name, country.Code, item.Link, country.MapId, country.MapName, country.Settings, date);
        values["mentions"] = Mentions(item.Challenge.MentionRoleIds);

        return CountryChallengeTemplate.Render(item.Challenge.Entry, values, date).Trim();
    }

    private static string ResultsEntry(EvaluatedChallenge challenge)
    {
        var post = challenge.Post;
        var settings = new GameSettings(post.TimeLimit, post.ForbidMoving, post.ForbidRotating, post.ForbidZooming);
        var values = EntryValues(
            post.ChallengeName, post.Country, post.CountryCode, ChallengeLink(post.ChallengeId), post.MapId, post.MapName,
            settings, post.Date);
        values["ranking"] = Ranking(challenge);

        // The entry describes the day the challenge was played, so its dates are that day's.
        return CountryChallengeTemplate.Render(challenge.Results.Entry, values, post.Date).Trim();
    }

    private static string Ranking(EvaluatedChallenge challenge)
    {
        if (challenge.Players.Count == 0)
        {
            return "No one participated :frowning2:";
        }

        var lines = challenge.Players
            .Take(challenge.Results.Top)
            .Select((player, i) =>
            {
                var points = i < challenge.Points.Count ? challenge.Points[i] : 0;
                var earned = points > 0 ? $" · +{points}" : string.Empty;
                return $"{Medal(i + 1)} {EscapeMarkdown(player.Nickname)} ({player.TotalScore}, {player.TotalDistance}){earned}";
            });

        return string.Join("\n", lines);
    }

    private static string ThreadName(CountryChallengePlan plan, DateOnly date, IReadOnlyList<AnnouncementItem> created)
    {
        var values = Values(
            ("names", JoinNames(DistinctNames(created))),
            ("day", Day(date)));

        var name = CountryChallengeTemplate.Render(plan.Announcement.Thread.Name, values, date).Trim();
        if (name.Length == 0)
        {
            name = "Country challenges";
        }

        return name.Length <= ThreadNameMaxLength ? name : name[..(ThreadNameMaxLength - 1)] + "…";
    }

    private static Dictionary<string, string> EntryValues(
        string name,
        string country,
        string? countryCode,
        string link,
        string mapId,
        string? mapName,
        GameSettings settings,
        DateOnly date)
    {
        return Values(
            ("name", name),
            ("country", country),
            ("flag", countryCode.ToFlagEmoji()),
            ("link", link),
            ("mapName", mapName ?? country),
            ("mapLink", MapLink(mapId)),
            ("settings", ChallengeSettingsText.Describe(settings)),
            ("mode", ChallengeSettingsText.Mode(settings)),
            ("timeLimit", ChallengeSettingsText.TimeLimit(settings.TimeLimit)),
            ("day", Day(date)));
    }

    /// <summary>A challenge playing several countries is named once, not once per country.</summary>
    private static List<string> DistinctNames(IEnumerable<AnnouncementItem> items) =>
        items.Select(i => i.Challenge.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Mentions(IEnumerable<ulong> roleIds) => string.Join(" ", roleIds.Distinct().Select(id => $"<@&{id}>"));

    private static string Day(DateOnly date) => date.DayOfWeek.ToString();

    private static Dictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
}
