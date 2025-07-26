namespace Constants;

/// <summary>
/// Class holding the configuration keys as constants.
/// </summary>
public static class ConfigKeys
{
    public const string PostgresConnectionString = "PostgreSQL";
    
    public const string DiscordBotTokenConfigurationKey = "Discord:BotToken";
    public const string DiscordServerIdConfigurationKey = "Discord:ServerId";

    public const string GeoGuessrTokenConfigurationKey = "GeoGuessr:NcfaToken";
    public const string GeoGuessrClubIdConfigurationKey = "GeoGuessr:ClubId";

    public const string ActivityCheckerFrequencyConfigurationKey = "ActivityChecker:CheckFrequency";
    public const string ActivityCheckerTextChannelIdConfigurationKey = "ActivityChecker:TextChannelId";
    public const string ActivityCheckerMinXpConfigurationKey = "ActivityChecker:MinXP";
    public const string ActivityCheckerMaxNumStrikesConfigurationKey = "ActivityChecker:MaxNumStrikes";
    public const string ActivityCheckerHistoryKeepTimeSpanConfigurationKey = "ActivityChecker:HistoryKeepTimeSpan";
    public const string ActivityCheckerCreateStrikeMaxRetryCountConfigurationKey = "ActivityChecker:CreateStrikeMaxRetryCount";
    
    public const string ClubLevelCheckerFrequencyConfigurationKey =  "ClubLevelChecker:CheckFrequency";
    public const string ClubLevelCheckerLevelUpMessageChannelIdConfigurationKey =  "ClubLevelChecker:LevelUpMessageChannelId";
    
    public const string SQLMigrateConfigurationKey = "SQL:Migrate";
}