using Constants;
using Discord;
using Discord.Interactions;
using Entities;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Club;

/// <summary>What a member picks in <c>/club-stats board-alerts</c> and on the panel's buttons.</summary>
public enum BoardAlertsChoice
{
    [ChoiceDisplay("Every new board and new week")]
    Always,

    [ChoiceDisplay("Only the next new board or new week")]
    Once,

    [ChoiceDisplay("Off")]
    Off
}

/// <summary>
/// The texts and buttons of the board alerts (#504): the member's state, and the private panel shown
/// from the button under <c>/club-stats board</c>. Pure, so every path words them the same.
/// </summary>
public static class MissionBoardAlertsPanel
{
    public const string ButtonLabel = "Board alerts";

    public static string ChoiceKey(BoardAlertsChoice choice) => choice switch
    {
        BoardAlertsChoice.Always => "always",
        BoardAlertsChoice.Once => "once",
        _ => "off"
    };

    public static BoardAlertsChoice? ParseChoiceKey(string key) => key switch
    {
        "always" => BoardAlertsChoice.Always,
        "once" => BoardAlertsChoice.Once,
        "off" => BoardAlertsChoice.Off,
        _ => null
    };

    public static MissionBoardSubscriptionMode? ModeOf(BoardAlertsChoice choice) => choice switch
    {
        BoardAlertsChoice.Always => MissionBoardSubscriptionMode.Always,
        BoardAlertsChoice.Once => MissionBoardSubscriptionMode.NextChangeOnly,
        _ => null
    };

    /// <summary>What the member gets now, in one sentence.</summary>
    public static string Describe(MissionBoardSubscriptionMode? mode, string? clubName = null)
    {
        var club = clubName is null ? "your club's" : $"**{clubName}**'s";
        return mode switch
        {
            MissionBoardSubscriptionMode.Always =>
                $"🔔 You get a DM whenever {club} next mission board is unlocked or a new week's board starts.",
            MissionBoardSubscriptionMode.NextChangeOnly =>
                $"🔔 You get one DM when {club} next mission board is unlocked or a new week's board starts, then the alerts stop.",
            _ => "🔕 Board alerts are off."
        };
    }

    /// <summary>Added below a subscription's confirmation.</summary>
    public const string SkipNote = "-# No DM is sent while you have already claimed a mission this claim cycle.";

    /// <summary>The private panel: the member's state and a button for each other choice.</summary>
    public static (string Text, MessageComponent Components) Build(MissionBoardSubscriptionMode? mode, string? note = null)
    {
        var text = Describe(mode);
        if (mode is not null)
        {
            text += $"\n{SkipNote}";
        }

        if (note is not null)
        {
            text = $"{note}\n{text}";
        }

        var components = new ComponentBuilder()
            .WithButton("Every new board", SetId(BoardAlertsChoice.Always), ButtonStyle.Primary, new Emoji("🔔"),
                disabled: mode == MissionBoardSubscriptionMode.Always)
            .WithButton("Next one only", SetId(BoardAlertsChoice.Once), ButtonStyle.Secondary, new Emoji("1️⃣"),
                disabled: mode == MissionBoardSubscriptionMode.NextChangeOnly)
            .WithButton("Off", SetId(BoardAlertsChoice.Off), ButtonStyle.Secondary, new Emoji("🔕"),
                disabled: mode is null)
            .Build();

        return (text, components);
    }

    private static string SetId(BoardAlertsChoice choice) =>
        $"{ComponentIds.MissionBoardAlertsSetButtonId}:{ChoiceKey(choice)}";
}
