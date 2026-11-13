namespace Entities;

/// <summary>Why a question's retrieval counts as weak.</summary>
public enum AiRetrievalGapReason
{
    /// <summary>The index offered nothing at all.</summary>
    NoExcerpts = 0,

    /// <summary>The question names a country, and no excerpt offered was from it.</summary>
    NothingFromTheCountry,

    /// <summary>The answer cited none of the excerpts it was offered.</summary>
    NothingCited
}

/// <summary>
/// A question whose retrieval was weak, recorded when it was asked: the only record of it once the
/// conversation is swept by retention. What the knowledge-gap report groups by country and topic.
/// </summary>
public class AiRetrievalGap : BaseEntity
{
    public int Id { get; private set; }

    public DateTimeOffset AskedAtUtc { get; private set; }

    /// <summary>The question, cut to 500 characters.</summary>
    public string Question { get; private set; } = string.Empty;

    /// <summary>The country the question is about, as the index spells it; null when it names none.</summary>
    public string? Country { get; private set; }

    /// <summary>The topic the question reads as (poles, bollards, …); null when none.</summary>
    public string? Topic { get; private set; }

    public AiRetrievalGapReason Reason { get; private set; }

    public const int MaxQuestionLength = 500;

    public static AiRetrievalGap Create(DateTimeOffset askedAtUtc, string question, string? country, string? topic, AiRetrievalGapReason reason) =>
        new()
        {
            AskedAtUtc = askedAtUtc,
            Question = question.Length <= MaxQuestionLength ? question : question[..MaxQuestionLength],
            Country = country,
            Topic = topic,
            Reason = reason
        };

    private AiRetrievalGap()
    {
    }
}
