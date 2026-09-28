using System.Globalization;
using System.Text.RegularExpressions;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.CountryChallenges.Rendering;

/// <summary>The templates of the country challenge file, each with the placeholders it may use.</summary>
public enum CountryChallengeTemplateKind
{
    AnnouncementMessage,
    AnnouncementEntry,
    ThreadName,
    ResultsMessage,
    ResultsEntry,
    LeaderboardMessage
}

/// <summary>
/// <c>{{placeholder}}</c> substitution for the country challenge messages. <c>{{date}}</c> is the only
/// placeholder that takes a format (<c>{{date:dd.MM.}}</c>); everything else is a plain value.
/// Placeholder names ignore case.
/// </summary>
public static partial class CountryChallengeTemplate
{
    public const string DefaultDateFormat = "yyyy-MM-dd";

    private const string DatePlaceholder = "date";

    private static readonly string[] EntryPlaceholders =
        ["name", "country", "flag", "link", "mapName", "mapLink", "settings", "mode", "timeLimit", "day", DatePlaceholder];

    private static readonly Dictionary<CountryChallengeTemplateKind, HashSet<string>> AllowedPlaceholders = new()
    {
        [CountryChallengeTemplateKind.AnnouncementMessage] = Set("mentions", "day", DatePlaceholder, "names", "challenges"),
        [CountryChallengeTemplateKind.AnnouncementEntry] = Set([.. EntryPlaceholders, "mentions"]),
        [CountryChallengeTemplateKind.ThreadName] = Set("names", "day", DatePlaceholder),
        [CountryChallengeTemplateKind.ResultsMessage] = Set("mentions", "day", DatePlaceholder, "results"),
        [CountryChallengeTemplateKind.ResultsEntry] = Set([.. EntryPlaceholders, "ranking"]),
        [CountryChallengeTemplateKind.LeaderboardMessage] = Set("mentions", "season", "day", DatePlaceholder, "leaderboard")
    };

    public static IReadOnlySet<string> PlaceholdersOf(CountryChallengeTemplateKind kind) => AllowedPlaceholders[kind];

    /// <summary>
    /// Replaces every placeholder. Values are inserted as they are and never scanned again, so a value
    /// that happens to contain <c>{{…}}</c> stays literal. Unknown placeholders are left untouched.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values, DateOnly date)
    {
        return PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;

            if (string.Equals(name, DatePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                var format = match.Groups[2].Success ? match.Groups[2].Value : DefaultDateFormat;
                return date.ToString(format, CultureInfo.InvariantCulture);
            }

            return values.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    /// <summary>What is wrong with a template: placeholders it may not use, and date formats that fail.</summary>
    public static IEnumerable<string> FindProblems(string template, CountryChallengeTemplateKind kind)
    {
        var allowed = AllowedPlaceholders[kind];

        foreach (Match match in PlaceholderRegex().Matches(template))
        {
            var name = match.Groups[1].Value;

            if (!allowed.Contains(name))
            {
                var available = allowed.Order(StringComparer.OrdinalIgnoreCase).Select(p => $"{{{{{p}}}}}");
                yield return $"{{{{{name}}}}} is not available here. Available: {string.Join(", ", available)}.";
                continue;
            }

            if (!match.Groups[2].Success)
            {
                continue;
            }

            if (!string.Equals(name, DatePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{{{{{name}}}}} does not take a format; only {{{{date}}}} does.";
                continue;
            }

            if (!IsValidDateFormat(match.Groups[2].Value))
            {
                yield return $"'{match.Groups[2].Value}' is not a valid date format (for example 'dd.MM.yyyy' or 'dddd').";
            }
        }

        // "{{ name }}" or "{{name}" would otherwise be posted literally without anyone noticing.
        if (PlaceholderRegex().Replace(template, string.Empty).Contains("{{", StringComparison.Ordinal))
        {
            yield return "Contains '{{' that is not a placeholder. Placeholders look like {{name}}, without spaces.";
        }
    }

    /// <summary>
    /// The pings written into the templates themselves, plus <paramref name="roleIds"/>. Only the raw
    /// templates are read — never rendered text — so nothing inserted into a message can ping anyone.
    /// </summary>
    public static MessageMentions ExtractMentions(IEnumerable<string> templates, IEnumerable<ulong> roleIds)
    {
        var roles = new HashSet<ulong>(roleIds);
        var users = new HashSet<ulong>();
        var everyone = false;

        foreach (var template in templates)
        {
            foreach (Match match in RoleMentionRegex().Matches(template))
            {
                if (ulong.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var roleId))
                {
                    roles.Add(roleId);
                }
            }

            foreach (Match match in UserMentionRegex().Matches(template))
            {
                if (ulong.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var userId))
                {
                    users.Add(userId);
                }
            }

            everyone |= EveryoneRegex().IsMatch(template);
        }

        return new MessageMentions([.. roles], [.. users], everyone);
    }

    private static bool IsValidDateFormat(string format)
    {
        try
        {
            _ = new DateOnly(2026, 12, 24).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{\{(\w+)(?::([^}]*))?\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<@&(\d+)>")]
    private static partial Regex RoleMentionRegex();

    [GeneratedRegex(@"<@!?(\d+)>")]
    private static partial Regex UserMentionRegex();

    // Standalone only: "someone@everyone.example" is an address, not a ping.
    [GeneratedRegex(@"(?<![\w@])@(everyone|here)(?!\w|\.\w)")]
    private static partial Regex EveryoneRegex();
}
