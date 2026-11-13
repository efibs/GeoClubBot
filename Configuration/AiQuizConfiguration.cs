using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// <c>/ai quiz</c>: a random captioned guide picture with the answer — country, guide section, the
/// clue — behind a spoiler, so members can practise with the pictures the club has chosen to index.
/// Costs no AI request.
/// </summary>
public class AiQuizConfiguration
{
    public const string SectionName = "AI:Quiz";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Pictures drawn per question to choose one clue from. Most of a draw is pictures under a bare
    /// heading, which make no question; a country with few guides needs a wide draw to find one.
    /// </summary>
    [Range(1, 200)]
    public int Draws { get; set; } = 24;

    /// <summary>
    /// Whether the quiz keeps to the channels the assistant answers in (<c>AI:AllowedChannelIds</c>).
    /// Off, or with no such list, it works anywhere the command is visible.
    /// </summary>
    public bool OnlyInAllowedChannels { get; set; } = true;
}
