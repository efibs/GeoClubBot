using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// <c>/ai review</c>: a played challenge's or game's rounds, or one Street View place, each with the
/// clues from the guides it showed, and what to study for the countries the player missed. One review
/// costs the rounds' searches (one embedding request per 32 of them), one vision request per round, and
/// the one chat request that writes it.
/// </summary>
public class AiRoundReviewConfiguration
{
    public const string SectionName = "AI:RoundReview";

    public bool Enabled { get; set; } = true;

    /// <summary>Rounds reviewed at most; a 5-round challenge is the usual case.</summary>
    [Range(1, 25)]
    public int MaxRounds { get; set; } = 10;

    /// <summary>
    /// Excerpts offered to the model in all, across the rounds; the rounds take turns. Each round offers
    /// the clues it was seen to show (up to six) and a few to look for, so five rounds want about thirty.
    /// </summary>
    [Range(1, 60)]
    public int MaxExcerpts { get; set; } = 30;

    /// <summary>How many places of the highscores are read to find the player. GeoGuessr lists the best first.</summary>
    [Range(1, 500)]
    public int HighscoresLimit { get; set; } = 100;

    /// <summary>A Street View model asked what each round's panorama shows; off until it has an address.</summary>
    public AiStreetViewGuesserConfiguration StreetViewGuesser { get; set; } = new();

    /// <summary>
    /// Each round's panorama looked at by a free vision model, which says which of the guides' clues it
    /// can see, so the review explains only clues the round showed (#480). One request per round.
    /// </summary>
    public AiClueSightConfiguration SightCheck { get; set; } = new();

    /// <summary>A round in the right country with at least this many points gets one line: there is little to learn from it.</summary>
    [Range(0, 5000)]
    public int WellPlayedPoints { get; set; } = 4500;

    /// <summary>One view of each round is posted with the review, the one the vision model found most telling.</summary>
    public bool ShowRoundViews { get; set; } = true;

    /// <summary>
    /// A Google Maps Street View link in a question is looked at like a review's round: placed, checked
    /// against its country's clues by the vision model, and offered to the answer. Two requests per question that has one.
    /// </summary>
    public bool StreetViewLinksInQuestions { get; set; } = true;
}

/// <summary>
/// How a round's panorama is checked against the guides: views of it read free from Google Street View's
/// thumbnail endpoint, and one request to a free vision model with the clues the guides give for the place.
/// </summary>
public class AiClueSightConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Clues from the guides the model is asked to look for per round: the country's most reliable ones,
    /// its roads and poles, what tells it from the country guessed, and the region's.
    /// </summary>
    [Range(1, 30)]
    public int CluesPerRound { get; set; } = 14;

    /// <summary>Excerpts taken from each of a round's searches before they are merged.</summary>
    [Range(1, 20)]
    public int ExcerptsPerSearch { get; set; } = 5;

    /// <summary>How long the model may take over one round's pictures; as long as an answer may (<c>AI:ResponseTimeoutSeconds</c>).</summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Clues the guides give that a round with the wrong country did not show, offered as what to look for next time.</summary>
    [Range(0, 5)]
    public int UnseenCluesForMissedRounds { get; set; } = 2;
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
