namespace Entities;

/// <summary>
/// What a vision model made of one indexed guide picture for the quiz (#491): the questions it can fairly
/// ask, each with its right answer, three wrong ones and a difficulty. A picture that gives its answer away
/// (a flag, a domain, a labelled map) or says too little on its own has no question of that kind; one with
/// none is kept all the same, so it is never rated twice.
/// </summary>
public class AiQuizItem : BaseEntity
{
    /// <summary>The index point the picture is.</summary>
    public Guid ChunkId { get; private set; }

    /// <summary>The rating's generation (<c>AI:Quiz:Rating:Version</c>); an older one is rated again.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// The picture and words the rating was made of (<c>QuizContentHash</c>). A guide re-indexed with new
    /// words under the same chunk id rates again rather than ask a question its picture no longer fits.
    /// </summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>The country the index files the picture under, as it spells it; null when none.</summary>
    public string? Country { get; private set; }

    /// <summary>The guide family, so a lesser-known quiz can leave PlonkIt out without reading the index.</summary>
    public string? SourceType { get; private set; }

    /// <summary>The model that rated it, for telling a bad batch apart later.</summary>
    public string? ModelId { get; private set; }

    public DateTimeOffset RatedAtUtc { get; private set; }

    public List<AiQuizItemQuestion> Questions { get; private set; } = [];

    public static AiQuizItem Create(
        Guid chunkId,
        int version,
        string contentHash,
        string? country,
        string? sourceType,
        string? modelId,
        DateTimeOffset ratedAtUtc,
        IEnumerable<AiQuizItemQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        return new AiQuizItem
        {
            ChunkId = chunkId,
            Version = version,
            ContentHash = contentHash,
            Country = country,
            SourceType = sourceType,
            ModelId = modelId,
            RatedAtUtc = ratedAtUtc,
            Questions = [.. questions.Select(question => question.For(chunkId))]
        };
    }

    /// <summary>The question of one kind, or null when the picture makes none of it.</summary>
    public AiQuizItemQuestion? QuestionOf(AiQuizQuestionKind kind) =>
        Questions.FirstOrDefault(question => question.Kind == kind);

    private AiQuizItem()
    {
    }
}

/// <summary>One question a picture makes: the right answer, three wrong ones, and how hard it is.</summary>
public class AiQuizItemQuestion
{
    public Guid ChunkId { get; private set; }

    public AiQuizQuestionKind Kind { get; private set; }

    public string Answer { get; private set; } = string.Empty;

    public List<string> Decoys { get; private set; } = [];

    public AiQuizDifficulty Difficulty { get; private set; }

    public static AiQuizItemQuestion Create(AiQuizQuestionKind kind, string answer, IEnumerable<string> decoys, AiQuizDifficulty difficulty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(decoys);

        return new AiQuizItemQuestion
        {
            Kind = kind,
            Answer = answer,
            Decoys = [.. decoys],
            Difficulty = difficulty
        };
    }

    internal AiQuizItemQuestion For(Guid chunkId) =>
        new()
        {
            ChunkId = chunkId,
            Kind = Kind,
            Answer = Answer,
            Decoys = [.. Decoys],
            Difficulty = Difficulty
        };

    private AiQuizItemQuestion()
    {
    }
}
