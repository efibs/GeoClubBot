namespace Constants;

/// <summary>
/// Configuration keys that cannot use the strongly-typed options pattern:
/// connection-string names (resolved via <c>IConfiguration.GetConnectionString</c>), cron schedule
/// keys read by the <c>ConfiguredCronJobAttribute</c> (attributes require compile-time constants),
/// and the <c>SQL:Migrate</c> flag read once at start-up before the host is built.
/// Everything else is bound to an options class in the Configuration project.
/// </summary>
public static class ConfigKeys
{
    public const string PostgresConnectionString = "PostgreSQL";
    public const string QDrantConnectionString = "QDrant";
    public const string LlmInferenceEndpointConnectionString = "LlmInferenceEndpoint";
    public const string EmbeddingEndpoint = "EmbeddingEndpoint";
    public const string CategorizationEndpoint = "CategorizationEndpoint";

    public const string GeoGuessrClubSyncScheduleConfigurationKey = "GeoGuessr:SyncSchedule";

    public const string ActivityCheckerCronScheduleConfigurationKey = "ActivityChecker:Schedule";

    public const string ClubLevelCheckerCronScheduleConfigurationKey = "ClubLevelChecker:Schedule";

    public const string DailyChallengesCronScheduleConfigurationKey = "DailyChallenges:Schedule";

    public const string DailyMissionReminderCronScheduleConfigurationKey = "DailyMissionReminder:Schedule";

    public const string DailyMissionLoggingCronScheduleConfigurationKey = "DailyMissionLogging:Schedule";

    public const string SqlMigrateConfigurationKey = "SQL:Migrate";
}
