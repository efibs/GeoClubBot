using Discord.Interactions;

namespace Infrastructure.InputAdapters.Commands;

[Group("member-activity", "Commands for interacting with the club member activity checker")]
public partial class ActivityModule : InteractionModuleBase<SocketInteractionContext>
{
    [Group("strike", "Commands all about the strikes of every player")]
    public partial class ActivityStrikeModule : InteractionModuleBase<SocketInteractionContext>;
}