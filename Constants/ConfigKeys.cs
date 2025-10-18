namespace Constants;

/// <summary>
/// Class holding the configuration keys as constants.
/// </summary>
public static class ConfigKeys
{
    public const string PostgresConnectionString = "PostgreSQL";
    public const string QDrantConnectionString = "QDrant";
    public const string LLMInferenceEndpointConnectionString = "LLMInferenceEndpoint";
    public const string EmbeddingEndpoint = "EmbeddingEndpoint";

    public const string AILLMModelNameConfigurationKey = "AI:LLMModel";
    public const string EmbeddingModelNameConfigurationKey = "AI:EmbeddingModel";
    
    public const string DiscordBotTokenConfigurationKey = "Discord:BotToken";
    public const string DiscordServerIdConfigurationKey = "Discord:ServerId";
    public const string DiscordWelcomeMessageConfigurationKey = "Discord:WelcomeMessage";
    public const string DiscordWelcomeTextChannelIdConfigurationKey = "Discord:WelcomeTextChannelId";

    public const string GeoGuessrTokenConfigurationKey = "GeoGuessr:NcfaToken";
    public const string GeoGuessrClubIdConfigurationKey = "GeoGuessr:ClubId";
    
    public const string GeoGuessrClubSyncScheduleConfigurationKey = "GeoGuessr:Club:SyncSchedule";
    
    public const string ActivityCheckerCronScheduleConfigurationKey = "ActivityChecker:Schedule";
    public const string ActivityCheckerTextChannelIdConfigurationKey = "ActivityChecker:TextChannelId";
    public const string ActivityCheckerMinXpConfigurationKey = "ActivityChecker:MinXP";
    public const string ActivityCheckerMaxNumStrikesConfigurationKey = "ActivityChecker:MaxNumStrikes";
    public const string ActivityCheckerHistoryKeepTimeSpanConfigurationKey = "ActivityChecker:HistoryKeepTimeSpan";
    public const string ActivityCheckerCreateStrikeMaxRetryCountConfigurationKey = "ActivityChecker:CreateStrikeMaxRetryCount";
    public const string ActivityCheckerStrikeDecayTimeSpanConfigurationKey = "ActivityChecker:StrikeDecayTimeSpan";
    
    public const string ActivityRewardTextChannelIdConfigurationKey = "ActivityReward:TextChannelId";
    public const string ActivityRewardMvpRoleIdConfigurationKey = "ActivityReward:MvpRoleId";
    
    public const string ClubLevelCheckerCronScheduleConfigurationKey =  "ClubLevelChecker:Schedule";
    public const string ClubLevelCheckerLevelUpMessageChannelIdConfigurationKey =  "ClubLevelChecker:LevelUpMessageChannelId";
    
    public const string DailyChallengesCronScheduleConfigurationKey = "DailyChallenges:Schedule";
    public const string DailyChallengesTextChannelIdConfigurationKey = "DailyChallenges:TextChannelId";
    public const string DailyChallengesConfigurationFilePathConfigurationKey = "DailyChallenges:ConfigurationFilePath";
    public const string DailyChallengesFirstRoleIdConfigurationKey = "DailyChallenges:FirstRoleId";
    public const string DailyChallengesSecondRoleIdConfigurationKey = "DailyChallenges:SecondRoleId";
    public const string DailyChallengesThirdRoleIdConfigurationKey = "DailyChallenges:ThirdRoleId";
    
    public const string GeoGuessrAccountLinkingAdminChannelIdConfigurationKey = "GeoGuessrAccountLinking:AdminChannelId";
    public const string GeoGuessrAccountLinkingHasLinkedRoleIdConfigurationKey = "GeoGuessrAccountLinking:HasLinkedRoleId";
    public const string GeoGuessrAccountLinkingClubMemberRoleIdConfigurationKey = "GeoGuessrAccountLinking:ClubMemberRoleId";

    public const string MemberPrivateChannelsCategoryIdConfigurationKey = "MemberPrivateChannels:CategoryId";
    public const string MemberPrivateChannelsDescriptionConfigurationKey = "MemberPrivateChannels:Description";
    
    public const string SelfRolesTextChannelIdConfigurationKey = "SelfRoles:TextChannelId";
    public const string SelfRolesRolesConfigurationKey = "SelfRoles:Roles";
    
    
    public const string SqlMigrateConfigurationKey = "SQL:Migrate";
}