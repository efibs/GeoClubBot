using Constants;
using Discord.Interactions;

namespace Infrastructure.InputAdapters.Interactions;

public class CompleteAccountLinkModal : IModal
{
    public string Title => "Confirm account linking";
    
    [InputLabel("One Time Password")]
    [ModalTextInput(
        ComponentIds.GeoGuessrAccountLinkingCompleteOneTimePasswordTextInputId, 
        placeholder: "Password", 
        minLength: StringLengthConstants.AccountLinkingRequestOneTimePasswordLength, 
        maxLength: StringLengthConstants.AccountLinkingRequestOneTimePasswordLength
    )]
    public string OneTimePassword { get; set; } = string.Empty;
}