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
    public const int CountryChallengeNameMaxLength = 100;
    public const int CountryChallengeCountryNameMaxLength = 100;
    public const int CountryChallengeCountryCodeLength = 2;
    public const int CountryChallengeSeasonMaxLength = 64;
    public const int GeoGuessrMapIdMaxLength = 64;
    public const int GeoGuessrMapNameMaxLength = 128;

    /// <summary>
    /// A stored AI turn. Larger than Discord's 2000-character message cap because an assistant turn
    /// holds the whole answer, which is split across several messages when it is posted.
    /// </summary>
    public const int AiConversationContentMaxLength = 8000;

    public const int AiModelIdMaxLength = 128;

    /// <summary>Must match AiAnswerFeedback's own clamp; the Domain layer cannot reference this project.</summary>
    public const int AiFeedbackCommentMaxLength = 1000;

    public const int KnowledgeSourceTypeMaxLength = 32;
    public const int KnowledgeSourceNaturalKeyMaxLength = 256;
    public const int KnowledgeSourceUrlMaxLength = 1024;
    public const int KnowledgeSourceTitleMaxLength = 256;
    /// <summary>Must match KnowledgeSource's own clamp; the Domain layer cannot reference this project.</summary>
    public const int KnowledgeSourceStatusReasonMaxLength = 512;
}
