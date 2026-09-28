using Constants;
using UseCases.UseCases.CountryChallenges.Rendering;

namespace UseCases.UseCases.CountryChallenges.Configuration;

/// <summary>The outcome of resolving the file: a plan, or every problem found in it.</summary>
public sealed record CountryChallengePlanResolution(CountryChallengePlan? Plan, IReadOnlyList<string> Errors);

/// <summary>
/// Fills in every inherited value — built-in default, then the top of the file, then the challenge,
/// then a single country of a pool — and checks every rule on the way.
///
/// Every problem is collected rather than stopping at the first, and each names where it is
/// (<c>Challenges[3] "Small Country Sunday" › Pool[1] "Malta": MapId is required.</c>), so one look at the
/// preview fixes a file. A value is checked at the level that sets it, so a mistake at the top of the
/// file is reported once, not once per challenge inheriting it.
///
/// A disabled challenge's problems are only warnings: disabling an unfinished challenge is how one is
/// parked, and it must not stop every other challenge from being posted.
/// </summary>
public static class CountryChallengePlanResolver
{
    public const int MaxResultsTop = 25;
    public const int MaxLeaderboardTop = 50;
    public const int MaxPlaces = 25;
    public const int MaxAfterDays = 365;

    public static CountryChallengePlanResolution Resolve(CountryChallengesFile file)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        CheckId(errors, "ChannelId", file.ChannelId);

        var rootSettings = MergeSettings(CountryChallengeDefaults.Settings, file.Settings, "Settings", errors);

        var announcementMessage = Template(
            errors, "Announcement.Message", file.Announcement?.Message,
            CountryChallengeDefaults.AnnouncementMessage, CountryChallengeTemplateKind.AnnouncementMessage);
        var defaultEntry = Template(
            errors, "Announcement.Entry", file.Announcement?.Entry,
            CountryChallengeDefaults.AnnouncementEntry, CountryChallengeTemplateKind.AnnouncementEntry);
        var defaultMentionRoleIds = Ids(errors, "Announcement.MentionRoleIds", file.Announcement?.MentionRoleIds, []);
        var thread = ResolveThread(file.Announcement?.Thread, errors);

        var resultsMessage = Template(
            errors, "Results.Message", file.Results?.Message,
            CountryChallengeDefaults.ResultsMessage, CountryChallengeTemplateKind.ResultsMessage);
        var resultsMentionRoleIds = Ids(errors, "Results.MentionRoleIds", file.Results?.MentionRoleIds, []);
        var defaultResults = MergeResults(CountryChallengeDefaults.Results, ToChallengeResults(file.Results), "Results", errors);

        var leaderboard = ResolveLeaderboard(file, errors);

        var challenges = new List<ChallengePlan>();
        var disabledChallenges = new List<ChallengePlan>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = file.Challenges ?? [];

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var path = string.IsNullOrWhiteSpace(section.Name) ? $"Challenges[{i}]" : $"Challenges[{i}] \"{section.Name}\"";

            // The name is the challenge's identity in the database, so a clash is an error even when one of
            // the two is disabled.
            if (!string.IsNullOrWhiteSpace(section.Name) && !names.Add(section.Name.Trim()))
            {
                errors.Add($"{path}: another challenge already has this name. Names must be unique.");
            }

            var problems = new List<string>();
            var challenge = ResolveChallenge(
                section, path, rootSettings, file.ChannelId, defaultEntry, defaultMentionRoleIds, defaultResults, problems);

            var enabled = section.Enabled ?? true;
            if (!enabled)
            {
                warnings.AddRange(problems.Select(p => $"{p} (the challenge is disabled, so this is only a warning)"));
                if (challenge is not null)
                {
                    disabledChallenges.Add(challenge);
                }

                continue;
            }

            errors.AddRange(problems);
            if (challenge is not null)
            {
                challenges.Add(challenge);
            }
        }

        if (errors.Count > 0)
        {
            return new CountryChallengePlanResolution(null, errors);
        }

        if (challenges.Count == 0)
        {
            warnings.Add("No challenge is enabled, so nothing will be posted.");
        }

        var plan = new CountryChallengePlan(
            new AnnouncementPlan(announcementMessage, thread),
            new ResultsPlan(resultsMessage, resultsMentionRoleIds, defaultResults),
            leaderboard,
            challenges,
            warnings)
        {
            DisabledChallenges = disabledChallenges
        };

        return new CountryChallengePlanResolution(plan, []);
    }

    private static ChallengePlan? ResolveChallenge(
        ChallengeSection section,
        string path,
        GameSettings rootSettings,
        ulong? rootChannelId,
        string defaultEntry,
        IReadOnlyList<ulong> defaultMentionRoleIds,
        ChallengeResultsPlan defaultResults,
        List<string> problems)
    {
        var name = section.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            problems.Add($"{path}: Name is required.");
        }
        else if (name.Length > StringLengthConstants.CountryChallengeNameMaxLength)
        {
            problems.Add($"{path}: Name is longer than {StringLengthConstants.CountryChallengeNameMaxLength} characters.");
        }

        var days = (section.Days ?? []).ToHashSet();
        var dates = (section.Dates ?? []).ToHashSet();
        if (days.Count == 0 && dates.Count == 0)
        {
            problems.Add($"{path}: set Days and/or Dates, otherwise the challenge never runs.");
        }

        var settings = MergeSettings(rootSettings, section.Settings, $"{path} › Settings", problems);
        var countries = ResolveCountries(section, path, settings, problems);

        var channelId = section.ChannelId ?? rootChannelId;
        if (section.ChannelId is not null)
        {
            CheckId(problems, $"{path} › ChannelId", section.ChannelId);
        }
        else if (rootChannelId is null or 0)
        {
            problems.Add($"{path}: no channel. Set ChannelId at the top of the file or on the challenge.");
        }

        var mentionRoleIds = Ids(problems, $"{path} › MentionRoleIds", section.MentionRoleIds, defaultMentionRoleIds);
        var entry = Template(problems, $"{path} › Entry", section.Entry, defaultEntry, CountryChallengeTemplateKind.AnnouncementEntry);
        var results = MergeResults(defaultResults, section.Results, $"{path} › Results", problems);

        if (problems.Count > 0)
        {
            return null;
        }

        return new ChallengePlan(name, days, dates, countries, channelId!.Value, mentionRoleIds, entry, results);
    }

    private static List<CountryPlan> ResolveCountries(
        ChallengeSection section,
        string path,
        GameSettings challengeSettings,
        List<string> problems)
    {
        if (section.Country is not null && section.Pool is not null)
        {
            problems.Add($"{path}: set either Country or Pool, not both.");
            return [];
        }

        if (section.Country is not null)
        {
            var country = ResolveCountry(section.Country, $"{path} › Country", challengeSettings, problems);
            return country is null ? [] : [country];
        }

        if (section.Pool is null || section.Pool.Count == 0)
        {
            problems.Add($"{path}: set Country (always the same country) or a non-empty Pool (a different one each time).");
            return [];
        }

        var countries = new List<CountryPlan>(section.Pool.Count);
        var countryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < section.Pool.Count; i++)
        {
            var entry = section.Pool[i];
            var entryPath = string.IsNullOrWhiteSpace(entry.Name) ? $"{path} › Pool[{i}]" : $"{path} › Pool[{i}] \"{entry.Name}\"";

            // The pool rotation tells countries apart by name, so two entries with one name would be one.
            if (!string.IsNullOrWhiteSpace(entry.Name) && !countryNames.Add(entry.Name.Trim()))
            {
                problems.Add($"{entryPath}: the pool already has a country with this name.");
            }

            var country = ResolveCountry(entry, entryPath, challengeSettings, problems);
            if (country is not null)
            {
                countries.Add(country);
            }
        }

        return countries;
    }

    private static CountryPlan? ResolveCountry(
        CountrySection section,
        string path,
        GameSettings challengeSettings,
        List<string> problems)
    {
        var before = problems.Count;

        var name = section.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            problems.Add($"{path}: Name is required.");
        }
        else if (name.Length > StringLengthConstants.CountryChallengeCountryNameMaxLength)
        {
            problems.Add($"{path}: Name is longer than {StringLengthConstants.CountryChallengeCountryNameMaxLength} characters.");
        }

        string? code = null;
        if (section.Code is not null)
        {
            code = section.Code.Trim().ToUpperInvariant();
            if (code.Length != StringLengthConstants.CountryChallengeCountryCodeLength || !code.All(char.IsAsciiLetterUpper))
            {
                problems.Add($"{path}: Code '{section.Code}' is not a two-letter country code such as 'MN'.");
            }
        }

        var mapId = section.MapId?.Trim() ?? string.Empty;
        if (mapId.Length == 0)
        {
            problems.Add($"{path}: MapId is required.");
        }
        else if (mapId.Any(char.IsWhiteSpace) || mapId.Length > StringLengthConstants.GeoGuessrMapIdMaxLength)
        {
            problems.Add($"{path}: MapId '{mapId}' is not a GeoGuessr map id (the part after /maps/ in the map's link).");
        }

        var mapName = string.IsNullOrWhiteSpace(section.MapName) ? null : section.MapName.Trim();
        if (mapName is not null && mapName.Length > StringLengthConstants.GeoGuessrMapNameMaxLength)
        {
            problems.Add($"{path}: MapName is longer than {StringLengthConstants.GeoGuessrMapNameMaxLength} characters.");
        }

        var settings = MergeSettings(challengeSettings, section.Settings, $"{path} › Settings", problems);

        return problems.Count > before ? null : new CountryPlan(name, code, mapId, mapName, settings);
    }

    private static ThreadPlan ResolveThread(ThreadSection? section, List<string> errors)
    {
        var defaults = CountryChallengeDefaults.Thread;
        var name = Template(errors, "Announcement.Thread.Name", section?.Name, defaults.Name, CountryChallengeTemplateKind.ThreadName);

        return new ThreadPlan(section?.Enabled ?? defaults.Enabled, name, section?.AutoArchive ?? defaults.AutoArchive);
    }

    private static LeaderboardPlan ResolveLeaderboard(CountryChallengesFile file, List<string> errors)
    {
        var section = file.Leaderboard;
        var enabled = section?.Enabled ?? CountryChallengeDefaults.LeaderboardEnabled;

        var season = section?.Season?.Trim() ?? CountryChallengeDefaults.LeaderboardSeason;
        if (season.Length == 0)
        {
            errors.Add("Leaderboard.Season must not be empty.");
        }
        else if (season.Length > StringLengthConstants.CountryChallengeSeasonMaxLength)
        {
            errors.Add($"Leaderboard.Season is longer than {StringLengthConstants.CountryChallengeSeasonMaxLength} characters.");
        }

        var days = (section?.Days ?? [.. CountryChallengeDefaults.LeaderboardDays]).ToHashSet();
        var dates = (section?.Dates ?? []).ToHashSet();

        var channelId = section?.ChannelId ?? file.ChannelId;
        if (section?.ChannelId is not null)
        {
            CheckId(errors, "Leaderboard.ChannelId", section.ChannelId);
        }
        else if (enabled && file.ChannelId is null or 0)
        {
            errors.Add("Leaderboard: no channel. Set ChannelId at the top of the file or in Leaderboard.");
        }

        var top = section?.Top ?? CountryChallengeDefaults.LeaderboardTop;
        if (top is < 1 or > MaxLeaderboardTop)
        {
            errors.Add($"Leaderboard.Top must be between 1 and {MaxLeaderboardTop}.");
        }

        var message = Template(
            errors, "Leaderboard.Message", section?.Message,
            CountryChallengeDefaults.LeaderboardMessage, CountryChallengeTemplateKind.LeaderboardMessage);
        var roleIds = Ids(errors, "Leaderboard.RoleIds", section?.RoleIds, []);
        if (roleIds.Count > MaxPlaces)
        {
            errors.Add($"Leaderboard.RoleIds may name at most {MaxPlaces} roles.");
        }

        var mentionRoleIds = Ids(errors, "Leaderboard.MentionRoleIds", section?.MentionRoleIds, []);

        return new LeaderboardPlan(enabled, season, days, dates, channelId ?? 0, top, message, roleIds, mentionRoleIds);
    }

    private static ChallengeResultsSection? ToChallengeResults(ResultsSection? section) =>
        section is null
            ? null
            : new ChallengeResultsSection
            {
                Enabled = section.Enabled,
                AfterDays = section.AfterDays,
                Post = section.Post,
                ChannelId = section.ChannelId,
                Top = section.Top,
                Entry = section.Entry,
                Points = section.Points,
                RoleIds = section.RoleIds
            };

    private static ChallengeResultsPlan MergeResults(
        ChallengeResultsPlan parent,
        ChallengeResultsSection? section,
        string path,
        List<string> problems)
    {
        if (section is null)
        {
            return parent;
        }

        if (section.AfterDays is < 0 or > MaxAfterDays)
        {
            problems.Add($"{path}.AfterDays must be between 0 and {MaxAfterDays}.");
        }

        if (section.Top is < 1 or > MaxResultsTop)
        {
            problems.Add($"{path}.Top must be between 1 and {MaxResultsTop}.");
        }

        if (section.ChannelId is not null)
        {
            CheckId(problems, $"{path}.ChannelId", section.ChannelId);
        }

        if (section.Points is not null)
        {
            if (section.Points.Count > MaxPlaces)
            {
                problems.Add($"{path}.Points may list at most {MaxPlaces} places.");
            }

            if (section.Points.Any(p => p < 0))
            {
                problems.Add($"{path}.Points must not be negative.");
            }
        }

        var roleIds = Ids(problems, $"{path}.RoleIds", section.RoleIds, parent.RoleIds);
        if (roleIds.Count > MaxPlaces)
        {
            problems.Add($"{path}.RoleIds may name at most {MaxPlaces} roles.");
        }

        var entry = Template(problems, $"{path}.Entry", section.Entry, parent.Entry, CountryChallengeTemplateKind.ResultsEntry);

        return new ChallengeResultsPlan(
            section.Enabled ?? parent.Enabled,
            section.AfterDays ?? parent.AfterDays,
            section.Post ?? parent.Post,
            section.ChannelId ?? parent.ChannelId,
            section.Top ?? parent.Top,
            entry,
            section.Points ?? parent.Points,
            roleIds);
    }

    private static GameSettings MergeSettings(GameSettings parent, GameSettingsSection? section, string path, List<string> problems)
    {
        if (section is null)
        {
            return parent;
        }

        if (section.TimeLimit < 0)
        {
            problems.Add($"{path}.TimeLimit must not be negative (0 means no limit).");
        }

        return new GameSettings(
            section.TimeLimit ?? parent.TimeLimit,
            section.ForbidMoving ?? parent.ForbidMoving,
            section.ForbidRotating ?? parent.ForbidRotating,
            section.ForbidZooming ?? parent.ForbidZooming);
    }

    private static string Template(
        List<string> problems,
        string path,
        string? value,
        string fallback,
        CountryChallengeTemplateKind kind)
    {
        if (value is null)
        {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{path} must not be empty.");
            return fallback;
        }

        problems.AddRange(CountryChallengeTemplate.FindProblems(value, kind).Select(p => $"{path}: {p}"));
        return value;
    }

    private static IReadOnlyList<ulong> Ids(List<string> problems, string path, List<ulong>? ids, IReadOnlyList<ulong> fallback)
    {
        if (ids is null)
        {
            return fallback;
        }

        if (ids.Contains(0UL))
        {
            problems.Add($"{path} contains 0, which is not a Discord id.");
        }

        // Kept as written: RoleIds are indexed by place, so the same role may deliberately appear twice.
        return [.. ids];
    }

    private static void CheckId(List<string> problems, string path, ulong? id)
    {
        if (id == 0)
        {
            problems.Add($"{path} is 0, which is not a Discord id.");
        }
    }
}
