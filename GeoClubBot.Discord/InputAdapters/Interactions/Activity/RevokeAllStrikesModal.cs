using Constants;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Activity;

public class RevokeAllStrikesModal : IModal
{
    public string Title => "Revoke ALL active strikes?";

    [InputLabel("Type 'confirm' to revoke every active strike:")]
    [ModalTextInput(
        ComponentIds.RevokeAllStrikesConfirmTextInputId,
        placeholder: "confirm",
        minLength: 7,
        maxLength: 7
    )]
    public string ConfirmText { get; set; } = string.Empty;
}
