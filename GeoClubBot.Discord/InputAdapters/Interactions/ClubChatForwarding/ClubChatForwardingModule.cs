using System.Globalization;
using System.Text;
using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubChatForwarding;

namespace GeoClubBot.Discord.InputAdapters.Interactions.ClubChatForwarding;

/// <summary>
/// Right-click a message → Apps → "📢 Send to club chat": a form opens with the message as plain
/// text (editable), the clubs to send it to, how many chat messages it takes and who forwarded it
/// before. Submitting sends it into each chosen club's in-game chat and reports per club.
/// </summary>
/// <remarks>
/// Hidden from everyone but administrators by default; the use cases check the configured roles
/// again, since server admins can grant the command to anyone in Discord's settings.
/// </remarks>
[CommandContextType(InteractionContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public partial class ClubChatForwardingModule(ISender mediator, ILogger<ClubChatForwardingModule> logger)
    : ClubBotInteractionModule(mediator, logger)
{
    [MessageCommand("📢 Send to club chat")]
    public async Task OpenFormAsync(IMessage message)
    {
        try
        {
            var draft = await Mediator.Send(new PrepareClubChatForwardQuery(
                message.Id,
                Context.User.Id,
                DiscordTextFlattener.Flatten(ContentOf(message), Names(message)),
                AuthorName(message),
                message.Channel?.Name ?? "unknown-channel")).ConfigureAwait(false);

            if (draft.IsFailure)
            {
                await RespondAsync(FailureText(draft.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (draft.Value.Clubs.Count == 0)
            {
                await RespondAsync("No club takes forwarded messages.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            // A modal must be the interaction's first response, so no deferring here.
            await Context.Interaction.RespondWithModalAsync(BuildForm(message.Id, draft.Value)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFormOpenFailed(Logger, ex, message.Id);
            await RespondAsync("Failed to open the form.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction($"{ComponentIds.ClubChatForwardModalId}:*", true)]
    public Task FormSubmittedAsync(string messageIdText, ClubChatForwardModal form) =>
        ExecuteAsync(async ct =>
        {
            if (!ulong.TryParse(messageIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
            {
                await FollowupAsync("That form has expired.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var clubIds = ChosenClubs();
            var result = await Mediator
                .Send(new ForwardToClubChatCommand(messageId, Context.User.Id, form.Text, clubIds), ct)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await FollowupAsync(FailureText(result.Error), ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(ClubChatForwardReportText.Render(result.Value), ephemeral: true, allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
        }, ephemeral: true, failureMessage: "Failed to forward the message.");

    private static Modal BuildForm(ulong messageId, ClubChatForwardDraft draft)
    {
        var text = draft.Text.Length > ClubChatForwardModalLimits.TextMaxLength
            ? draft.Text[..ClubChatForwardModalLimits.TextMaxLength]
            : draft.Text;

        var clubs = new CheckboxGroupBuilder()
            .WithCustomId(ComponentIds.ClubChatForwardClubsInputId)
            .WithMinValues(1)
            .WithMaxValues(Math.Min(draft.Clubs.Count, ClubChatForwardModalLimits.MaxClubs))
            .WithRequired(true);
        foreach (var club in draft.Clubs.Take(ClubChatForwardModalLimits.MaxClubs))
        {
            clubs.AddOption(Truncate(club.Name, 100), club.ClubId.ToString("D"), null, true);
        }

        return new ModalBuilder()
            .WithTitle("Send to club chat")
            .WithCustomId($"{ComponentIds.ClubChatForwardModalId}:{messageId}")
            .AddLabel("Message", new TextInputBuilder()
                .WithCustomId(ComponentIds.ClubChatForwardTextInputId)
                .WithStyle(TextInputStyle.Paragraph)
                .WithMaxLength(ClubChatForwardModalLimits.TextMaxLength)
                .WithRequired(true)
                .WithValue(text), "Edit freely. Long text is split into numbered messages.")
            .AddCheckBoxGroup("Clubs", clubs, "Each club's chat gets it from that club's account.")
            .AddTextDisplay(ClubChatForwardReportText.FormNotes(draft))
            .Build();
    }

    /// <summary>The checked clubs, read from the submission: the typed form doesn't know them.</summary>
    private List<Guid> ChosenClubs()
    {
        if (Context.Interaction is not SocketModal modal)
        {
            return [];
        }

        return modal.Data.Components
            .Where(c => c.CustomId == ComponentIds.ClubChatForwardClubsInputId)
            .SelectMany(c => c.Values ?? [])
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    /// <summary>The message's text, or for one without any (a bot's embed) the embed's title and description.</summary>
    private static string ContentOf(IMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return message.Content;
        }

        var embeds = new StringBuilder();
        foreach (var embed in message.Embeds)
        {
            if (!string.IsNullOrWhiteSpace(embed.Title))
            {
                embeds.Append(embed.Title).Append("\n\n");
            }

            if (!string.IsNullOrWhiteSpace(embed.Description))
            {
                embeds.Append(embed.Description).Append("\n\n");
            }
        }

        return embeds.ToString();
    }

    private DiscordNameLookup Names(IMessage message)
    {
        var guild = Context.Guild as SocketGuild;
        var mentioned = (message as SocketUserMessage)?.MentionedUsers ?? [];
        return new DiscordNameLookup(
            id => guild?.GetUser(id)?.DisplayName
                  ?? mentioned.FirstOrDefault(u => u.Id == id) switch
                  {
                      SocketGuildUser member => member.DisplayName,
                      { } user => user.GlobalName ?? user.Username,
                      null => null,
                  },
            id => guild?.GetRole(id)?.Name,
            id => guild?.GetChannel(id)?.Name);
    }

    private static string AuthorName(IMessage message) => message.Author switch
    {
        IGuildUser member => member.DisplayName,
        { } user => user.GlobalName ?? user.Username,
        _ => "unknown",
    };

    /// <summary>Unlike the general wording, says why a forward was refused: off, or not an admin.</summary>
    private static string FailureText(Utilities.Error error) =>
        error.Type == Utilities.ErrorType.Forbidden ? error.Message : FriendlyMessageFor(error);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    [LoggerMessage(LogLevel.Error, "Failed to open the club chat forward form for message {MessageId}.")]
    static partial void LogFormOpenFailed(ILogger logger, Exception ex, ulong messageId);
}

/// <summary>The form's notes and the report after sending.</summary>
public static class ClubChatForwardReportText
{
    public static string FormNotes(ClubChatForwardDraft draft)
    {
        var notes = new StringBuilder();
        notes.Append(draft.PartCount switch
        {
            null => "⚠️ As prefilled, this is too long to send: shorten it before sending.",
            1 => "Goes out as 1 chat message.",
            var count => string.Create(CultureInfo.InvariantCulture,
                $"GeoGuessr cuts chat messages at 300 characters, so this goes out as {count} numbered messages."),
        });

        foreach (var earlier in draft.EarlierForwards)
        {
            notes.Append(CultureInfo.InvariantCulture,
                $"\n⚠️ Already forwarded to **{earlier.ClubName}** by <@{earlier.ForwardedByDiscordUserId}> <t:{earlier.ForwardedAt.ToUnixTimeSeconds()}:R>");
            if (earlier.PartsConfirmed < earlier.PartCount)
            {
                notes.Append(CultureInfo.InvariantCulture, $" ({earlier.PartsConfirmed}/{earlier.PartCount} confirmed)");
            }

            notes.Append('.');
        }

        return notes.ToString();
    }

    public static string Render(ClubChatForwardReport report)
    {
        var text = new StringBuilder();
        text.Append(report.PartCount == 1
            ? "Forwarded as 1 chat message:"
            : string.Create(CultureInfo.InvariantCulture, $"Forwarded as {report.PartCount} chat messages:"));

        foreach (var (club, delivery) in report.Clubs)
        {
            text.Append("\n").Append(delivery.Status switch
            {
                ClubChatDeliveryStatus.Confirmed => $"✅ **{club.Name}**: sent, GeoGuessr confirmed it.",
                ClubChatDeliveryStatus.Unconfirmed when report.PartCount == 1 =>
                    $"⚠️ **{club.Name}**: sent, but GeoGuessr didn't confirm it. It may still have arrived, so check the chat before sending again.",
                ClubChatDeliveryStatus.Unconfirmed =>
                    $"⚠️ **{club.Name}**: GeoGuessr didn't confirm part {delivery.PartsSent} of {report.PartCount}, so the rest was held back. "
                    + "It may still have arrived, so check the chat before sending again.",
                ClubChatDeliveryStatus.Denied => $"❌ **{club.Name}**: not sent, GeoGuessr refused the club chat to the club's account.",
                _ when delivery.PartsSent > 0 =>
                    $"⚠️ **{club.Name}**: {delivery.PartsConfirmed} of {report.PartCount} parts confirmed, then {delivery.Detail ?? "the connection was lost"}. "
                    + "Check the chat before sending again.",
                _ => $"❌ **{club.Name}**: not sent, {delivery.Detail ?? "could not connect to GeoGuessr"}.",
            });
        }

        return text.ToString();
    }

}
