namespace Entities;

/// <summary>
/// One quiz question as posted (#491): the picture, the four options in the order shown, and everyone's
/// guess. Anyone may guess once; the answer is shown to all when the asker or an admin reveals it, or
/// once <see cref="RevealAtUtc"/> has passed.
/// </summary>
public class AiQuizRound : BaseEntity
{
    public const int OptionCount = 4;

    public Guid Id { get; private set; }

    /// <summary>The index point the picture is.</summary>
    public Guid ChunkId { get; private set; }

    public AiQuizQuestionKind Kind { get; private set; }

    public AiQuizDifficulty Difficulty { get; private set; }

    /// <summary>The options in the order they are shown.</summary>
    public List<string> Options { get; private set; } = [];

    public int CorrectIndex { get; private set; }

    /// <summary>The country the question is within, as shown ("Where in Mongolia…"); null for a country question.</summary>
    public string? CountryName { get; private set; }

    /// <summary>The guide's title, cited under the answer.</summary>
    public string Guide { get; private set; } = string.Empty;

    /// <summary>The section the picture stands in, cleaned; empty when it has none worth showing.</summary>
    public string Section { get; private set; } = string.Empty;

    /// <summary>What the guide says of the picture: the explanation shown with the answer.</summary>
    public string Explanation { get; private set; } = string.Empty;

    public string SourceUrl { get; private set; } = string.Empty;

    public string ImageUrl { get; private set; } = string.Empty;

    /// <summary>The filters it was drawn with, so "Another" draws alike: the country as the member wrote it.</summary>
    public string? AskedCountry { get; private set; }

    public bool LesserKnownOnly { get; private set; }

    /// <summary>The difficulty asked for; null when the member left it to the quiz.</summary>
    public AiQuizDifficulty? AskedDifficulty { get; private set; }

    public ulong ChannelId { get; private set; }

    /// <summary>The message the question was posted as; null until it is posted.</summary>
    public ulong? MessageId { get; private set; }

    public ulong AskedByUserId { get; private set; }

    public DateTimeOffset AskedAtUtc { get; private set; }

    /// <summary>When the answer is shown to everyone if nobody reveals it sooner.</summary>
    public DateTimeOffset RevealAtUtc { get; private set; }

    public DateTimeOffset? RevealedAtUtc { get; private set; }

    public List<AiQuizGuess> Guesses { get; private set; } = [];

    public bool IsRevealed => RevealedAtUtc is not null;

    public string CorrectOption => Options[CorrectIndex];

    public int RightGuesses => Guesses.Count(guess => guess.Correct);

    /// <param name="options">The options in the order to show them, the right one among them.</param>
    public static AiQuizRound Create(
        Guid chunkId,
        AiQuizQuestionKind kind,
        AiQuizDifficulty difficulty,
        IReadOnlyList<string> options,
        int correctIndex,
        string? countryName,
        string guide,
        string section,
        string explanation,
        string sourceUrl,
        string imageUrl,
        string? askedCountry,
        bool lesserKnownOnly,
        AiQuizDifficulty? askedDifficulty,
        ulong channelId,
        ulong askedByUserId,
        DateTimeOffset askedAtUtc,
        TimeSpan revealAfter)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count != OptionCount)
        {
            throw new ArgumentException($"A quiz question has {OptionCount} options.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(correctIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(correctIndex, OptionCount);

        return new AiQuizRound
        {
            Id = Guid.NewGuid(),
            ChunkId = chunkId,
            Kind = kind,
            Difficulty = difficulty,
            Options = [.. options],
            CorrectIndex = correctIndex,
            CountryName = countryName,
            Guide = guide,
            Section = section,
            Explanation = explanation,
            SourceUrl = sourceUrl,
            ImageUrl = imageUrl,
            AskedCountry = askedCountry,
            LesserKnownOnly = lesserKnownOnly,
            AskedDifficulty = askedDifficulty,
            ChannelId = channelId,
            AskedByUserId = askedByUserId,
            AskedAtUtc = askedAtUtc,
            RevealAtUtc = askedAtUtc + revealAfter
        };
    }

    public void AttachMessage(ulong messageId) => MessageId = messageId;

    /// <summary>The member's guess, or null when they have not guessed.</summary>
    public AiQuizGuess? GuessOf(ulong userId) => Guesses.FirstOrDefault(guess => guess.UserId == userId);

    /// <summary>
    /// Records a member's one guess, scored by the question's difficulty when right. Null when they have
    /// guessed already, the answer is out, or the option is none of the four.
    /// </summary>
    public AiQuizGuess? Guess(ulong userId, int optionIndex, DateTimeOffset nowUtc)
    {
        if (IsRevealed || optionIndex < 0 || optionIndex >= Options.Count || GuessOf(userId) is not null)
        {
            return null;
        }

        var correct = optionIndex == CorrectIndex;
        var guess = AiQuizGuess.Create(Id, userId, optionIndex, correct, correct ? (int)Difficulty : 0, nowUtc);
        Guesses.Add(guess);
        return guess;
    }

    /// <summary>Shows the answer to everyone; false when it already was.</summary>
    public bool Reveal(DateTimeOffset nowUtc)
    {
        if (IsRevealed)
        {
            return false;
        }

        RevealedAtUtc = nowUtc;
        return true;
    }

    private AiQuizRound()
    {
    }
}

/// <summary>One member's guess at a quiz question, and what it scored.</summary>
public class AiQuizGuess
{
    public Guid RoundId { get; private set; }

    public ulong UserId { get; private set; }

    public int OptionIndex { get; private set; }

    public bool Correct { get; private set; }

    /// <summary>The question's difficulty (1–3) when right, else 0.</summary>
    public int Points { get; private set; }

    public DateTimeOffset AnsweredAtUtc { get; private set; }

    internal static AiQuizGuess Create(Guid roundId, ulong userId, int optionIndex, bool correct, int points, DateTimeOffset answeredAtUtc) =>
        new()
        {
            RoundId = roundId,
            UserId = userId,
            OptionIndex = optionIndex,
            Correct = correct,
            Points = points,
            AnsweredAtUtc = answeredAtUtc
        };

    private AiQuizGuess()
    {
    }
}
