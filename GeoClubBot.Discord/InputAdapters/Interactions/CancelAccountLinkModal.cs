using Constants;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions;

public class CancelAccountLinkModal : IModal
{
    public string Title => "Confirm cancel of account linking process?";
    
    [InputLabel("Enter 'Confirm' here:")]
    [ModalTextInput(
        ComponentIds.GeoGuessrAccountLinkingCancelConfirmTextInputId, 
        placeholder: "Confirm", 
        minLength: 7, 
        maxLength: 7
    )]
    public string ConfirmText { get; set; } = string.Empty;
}