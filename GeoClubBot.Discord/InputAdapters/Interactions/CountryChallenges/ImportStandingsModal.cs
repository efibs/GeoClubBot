using Constants;
using Discord;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.CountryChallenges;

public class ImportStandingsModal : IModal
{
    public string Title => "Import country challenge standings";

    [InputLabel("One player per line: name, then points")]
    [ModalTextInput(
        ComponentIds.CountryChallengeImportStandingsTextInputId,
        TextInputStyle.Paragraph,
        placeholder: "Fibs 12\nhttps://www.geoguessr.com/user/<id> 7",
        maxLength: 4000
    )]
    public string Standings { get; set; } = string.Empty;
}
