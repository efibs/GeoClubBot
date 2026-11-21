namespace Entities;

/// <summary>What a quiz question asks of a picture (#491).</summary>
public enum AiQuizQuestionKind
{
    /// <summary>"Where is this?" — four countries.</summary>
    Country = 0,

    /// <summary>"Where in this country is this?" — four regions of the one country, for a regional clue.</summary>
    Region = 1,

    /// <summary>"What here is typical of this country?" — four things in the picture, one of them the clue.</summary>
    Clue = 2
}

/// <summary>How hard a question is, which is also what a right answer scores.</summary>
public enum AiQuizDifficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3
}
