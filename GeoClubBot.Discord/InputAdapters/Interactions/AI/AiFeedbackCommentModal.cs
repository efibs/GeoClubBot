using Constants;
using Discord;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.AI;

public class AiFeedbackCommentModal : IModal
{
    public string Title => "Feedback on this answer";

    /// <summary>
    /// Optional, because the verdict alone is already useful and demanding an explanation is the
    /// quickest way to stop people giving one at all.
    /// </summary>
    [InputLabel("What was good or bad about it?")]
    [ModalTextInput(
        ComponentIds.AiFeedbackCommentTextInputId,
        TextInputStyle.Paragraph,
        placeholder: "Optional — what it got wrong, or what was useful",
        maxLength: StringLengthConstants.AiFeedbackCommentMaxLength
    )]
    [RequiredInput(false)]
    public string? Comment { get; set; }
}
