namespace Constants;

public static class StringLengthConstants
{
    public const int GeoGuessrClubNameMaxLength = 64;
    public const int GeoGuessrPlayerNicknameMaxLength = 30;
    public const int GeoGuessrUserIdLength = 24;
    public const int GeoGuessrChallengeIdLength = 16;
    public const int AccountLinkingRequestOneTimePasswordLength = 18;
    public const int TimeZoneIdMaxLength = 64;
    public const int DailyMissionReminderCustomMessageMaxLength = 500;
    public const int DailyMissionTypeMaxLength = 32;
    public const int DailyMissionGameModeMaxLength = 32;
    public const int DailyMissionRewardTypeMaxLength = 32;
    public const int DailyMissionMapSlugMaxLength = 128;
    public const int DailyMissionMapNameMaxLength = 128;

    /// <summary>
    /// A stored AI turn. Larger than Discord's 2000-character message cap because an assistant turn
    /// holds the whole answer, which is split across several messages when it is posted.
    /// </summary>
    public const int AiConversationContentMaxLength = 8000;

    public const int AiModelIdMaxLength = 128;
}
