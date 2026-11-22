using Constants;
using Discord;
using UseCases.OutputPorts.Discord;
using Utilities;

namespace GeoClubBot.Discord.OutputAdapters;

public class DiscordMissionBoardChangeMessages(DiscordDirectMessageAccess directMessages) : IMissionBoardChangeMessages
{
    public Task<Result> SendAsync(ulong discordUserId, string text, bool withUnsubscribeButton, CancellationToken cancellationToken = default)
    {
        var components = withUnsubscribeButton
            ? new ComponentBuilder()
                .WithButton("Stop board alerts", ComponentIds.MissionBoardUnsubscribeButtonId, ButtonStyle.Secondary, new Emoji("🔕"))
                .Build()
            : null;

        return directMessages.SendAsync(discordUserId, text, components);
    }
}
