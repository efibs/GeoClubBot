using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.CountryChallenges.Configuration;

/// <summary>
/// The values a country challenge gets when the file sets nothing, so that a file holding only a
/// channel and the challenges already posts complete messages.
/// </summary>
public static class CountryChallengeDefaults
{
    /// <summary>GeoGuessr's own default: moving allowed, no time limit.</summary>
    public static readonly GameSettings Settings = new(TimeLimit: 0, ForbidMoving: false, ForbidRotating: false, ForbidZooming: false);

    public const string AnnouncementMessage = "{{mentions}}\n# :earth_africa: {{day}}'s country challenges\n{{challenges}}";

    public const string AnnouncementEntry = "### {{flag}} {{name}}\n**{{country}}** · {{settings}}\n{{link}}";

    public static readonly ThreadPlan Thread = new(Enabled: false, Name: "{{names}} · {{date}}", AutoArchive: ThreadAutoArchive.OneDay);

    public const string ResultsMessage = "# :trophy: Country challenge results\n{{results}}";

    public static readonly ChallengeResultsPlan Results = new(
        Enabled: true,
        AfterDays: 1,
        Post: true,
        ChannelId: null,
        Top: 10,
        Entry: "### {{flag}} {{name}} · {{country}}\n{{ranking}}",
        Points: [3, 2, 1],
        RoleIds: []);

    public const bool LeaderboardEnabled = true;

    public const string LeaderboardSeason = "Season 1";

    /// <summary>
    /// Monday: the first run at which every challenge of the previous week has been evaluated, as long as
    /// results arrive the day after.
    /// </summary>
    public static readonly DayOfWeek[] LeaderboardDays = [DayOfWeek.Monday];

    public const int LeaderboardTop = 15;

    public const string LeaderboardMessage = "# :bar_chart: Country challenge leaderboard · {{season}}\n{{leaderboard}}";
}
