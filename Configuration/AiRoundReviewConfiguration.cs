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

    /// <summary>A Street View model asked what each round's panorama shows; off until it has an address.</summary>
    public AiStreetViewGuesserConfiguration StreetViewGuesser { get; set; } = new();
}

/// <summary>
/// GeoNN, the maintainer's own GeoGuessr model, served by its <c>scripts/play.py</c> on a machine with a
/// GPU: given a round's panorama it says which countries it shows, and how sure it is. The review uses
/// that to tell a round whose country was there to see from one even the model could not place. Costs no
/// AI request; GeoNN fetches the panorama from Google Street View itself, as its own game does.
/// </summary>
public class AiStreetViewGuesserConfiguration
{
    /// <summary>Where GeoNN's <c>play.py</c> answers (<c>http://host:8765</c>); empty, the review does without it.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The exported model to ask (a folder of GeoNN's <c>models/</c>); empty, the newest it lists.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>How long one round's reading may take. The first loads the model onto the GPU (5 s here), the rest take under one.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Countries of each reading shown to the model, the likeliest first.</summary>
    [Range(1, 10)]
    public int CountriesShown { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
