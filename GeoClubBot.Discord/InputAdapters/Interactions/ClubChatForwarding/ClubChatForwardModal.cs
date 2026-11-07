using Constants;
using Discord;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.ClubChatForwarding;

/// <summary>
/// Reads the forward form back. The form itself is built per message in
/// <see cref="ClubChatForwardingModule"/>, because its clubs and notes depend on the message; this
/// only names the text field. The clubs are read from the submission directly.
/// </summary>
public class ClubChatForwardModal : IModal
{
    public string Title => "Send to club chat";

    [InputLabel("Message")]
    [ModalTextInput(ComponentIds.ClubChatForwardTextInputId, TextInputStyle.Paragraph, maxLength: ClubChatForwardModalLimits.TextMaxLength)]
    public string Text { get; set; } = string.Empty;
}

public static class ClubChatForwardModalLimits
{
    /// <summary>Discord's limit for a text input.</summary>
    public const int TextMaxLength = 4000;

    /// <summary>Discord's limit for a checkbox group.</summary>
    public const int MaxClubs = 10;
}
