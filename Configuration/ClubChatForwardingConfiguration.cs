using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// Forwards a Discord message into the clubs' in-game chats: right-click it → Apps →
/// "📢 Send to club chat". Each club's chat is written to as the account of its own
/// <see cref="GeoGuessrClubEntry.NcfaToken"/>; a club opts out with <see cref="GeoGuessrClubEntry.ForwardChat"/>.
/// </summary>
public class ClubChatForwardingConfiguration : IValidatableObject
{
    public const string SectionName = "ClubChatForwarding";

    /// <summary>The placeholder every template must contain.</summary>
    public const string TextPlaceholder = "{text}";

    public bool Enabled { get; set; }

    /// <summary>Roles that may forward, besides members with Discord's Administrator permission.</summary>
    public List<ulong> AdminRoleIds { get; set; } = [];

    /// <summary>
    /// What the form is prefilled with. Placeholders: <c>{text}</c> (the message as plain text),
    /// <c>{author}</c> (its author's server display name), <c>{channel}</c> (the channel's name).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Template { get; set; } =
        "{text}\n\nForwarded by {author} from our Discord server's announcement channel";

    /// <summary>A text that needs more chat messages than this is refused.</summary>
    [Range(1, 20)]
    public int MaxParts { get; set; } = 5;

    /// <summary>GeoGuessr silently cuts a chat message to 300 characters, so longer text is split into parts.</summary>
    [Range(20, 2000)]
    public int MaxMessageLength { get; set; } = 300;

    /// <summary>How long to wait for GeoGuessr to echo a sent part back, which is what confirms it arrived.</summary>
    public TimeSpan EchoTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Template) && !Template.Contains(TextPlaceholder, StringComparison.Ordinal))
        {
            yield return new ValidationResult($"{nameof(Template)} must contain {TextPlaceholder}.", [nameof(Template)]);
        }

        if (EchoTimeout <= TimeSpan.Zero || EchoTimeout > TimeSpan.FromMinutes(1))
        {
            yield return new ValidationResult($"{nameof(EchoTimeout)} must be between 0 and 1 minute.", [nameof(EchoTimeout)]);
        }
    }
}
