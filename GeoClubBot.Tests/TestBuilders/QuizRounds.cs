using Entities;
using UseCases.OutputPorts.AI;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>Quiz rounds and the pictures they are drawn from (#491).</summary>
public static class QuizRounds
{
    public static readonly DateTimeOffset AskedAt = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    public static AiQuizRound Round(
        AiQuizQuestionKind kind = AiQuizQuestionKind.Country,
        AiQuizDifficulty difficulty = AiQuizDifficulty.Medium,
        string? countryName = null,
        ulong askedBy = 1,
        ulong channelId = 100,
        string explanation = "Estonian bollards carry a red reflector on a black band.",
        string? askedCountry = null,
        AiQuizDifficulty? askedDifficulty = null,
        bool lesserKnownOnly = false) =>
        AiQuizRound.Create(
            Guid.NewGuid(),
            kind,
            difficulty,
            ["Latvia", "Estonia", "Lithuania", "Finland"],
            correctIndex: 1,
            countryName,
            "PlonkIt",
            "Bollards",
            explanation,
            "https://www.plonkit.net/estonia#bollards",
            "https://bot.example/api/v1/ai/images/abc.jpg",
            askedCountry,
            lesserKnownOnly,
            askedDifficulty,
            channelId,
            askedBy,
            AskedAt,
            TimeSpan.FromMinutes(10));

    public static KnowledgeHit Picture(string text, string? country = "estonia", Guid? id = null, string sourceType = "plonkit") =>
        new(id ?? Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, text, "https://www.plonkit.net/estonia#bollards",
            $"https://bot.example/api/v1/ai/images/{Guid.NewGuid():N}.jpg", "PlonkIt", country, "PlonkIt > Estonia > Bollards", null, 0,
            sourceType, "estonia", "1");
}
