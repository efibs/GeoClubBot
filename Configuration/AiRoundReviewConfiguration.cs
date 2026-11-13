using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// <c>/ai review</c>: a played challenge's rounds, each with the clues from the guides that point to
/// its location, and what to study for the countries the player missed. One review costs two AI
/// requests — the rounds' searches in one embedding request, then the one chat request — plus
/// whatever tool rounds the model takes, like a question.
/// </summary>
public class AiRoundReviewConfiguration
{
    public const string SectionName = "AI:RoundReview";

    public bool Enabled { get; set; } = true;

    /// <summary>Rounds reviewed at most; a 5-round challenge is the usual case.</summary>
    [Range(1, 25)]
    public int MaxRounds { get; set; } = 10;

    /// <summary>Guide excerpts searched for per round. Two or three per round keep a five-round review within a question's excerpts.</summary>
    [Range(1, 10)]
    public int ExcerptsPerRound { get; set; } = 3;

    /// <summary>Excerpts offered to the model in all, across the rounds; the rounds take turns.</summary>
    [Range(1, 40)]
    public int MaxExcerpts { get; set; } = 12;

    /// <summary>How many places of the highscores are read to find the player. GeoGuessr lists the best first.</summary>
    [Range(1, 500)]
    public int HighscoresLimit { get; set; } = 100;
}
