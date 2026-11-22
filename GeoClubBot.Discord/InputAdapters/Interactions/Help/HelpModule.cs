using Constants;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>
/// <c>/help</c> and <c>/manual</c>: what the bot can do, built from the commands themselves
/// (<see cref="BotManual"/>). Each member sees only the features that are on and the commands they may run.
/// Nothing is read from the database, so the reply is sent straight away rather than deferred.
/// </summary>
[NotInManual]
[CommandContextType(InteractionContextType.Guild)]
public sealed partial class HelpModule(IBotManual manual, ILogger<HelpModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string FeatureDescription = "Go straight to one feature's commands";
    private const string PublicDescription = "Post it in this channel for everyone, instead of only for you";

    [SlashCommand("help", "What I can do: every feature and its commands")]
    public Task HelpAsync(
        [Autocomplete(typeof(HelpFeatureAutocompleteHandler))][Summary(description: FeatureDescription)] string? feature = null,
        [Summary("public", PublicDescription)] bool isPublic = false) =>
        ShowAsync(feature, isPublic);

    [SlashCommand("manual", "What I can do: every feature and its commands (same as /help)")]
    public Task ManualAsync(
        [Autocomplete(typeof(HelpFeatureAutocompleteHandler))][Summary(description: FeatureDescription)] string? feature = null,
        [Summary("public", PublicDescription)] bool isPublic = false) =>
        ShowAsync(feature, isPublic);

    /// <summary>
    /// A pick in the menu turns the member's own (ephemeral) message to that page. A public message stays as
    /// it was posted for everyone: whoever picks gets the page for themselves.
    /// </summary>
    [ComponentInteraction(ComponentIds.HelpFeatureMenuId, ignoreGroupNames: true)]
    public Task FeaturePickedAsync(string[] picked) =>
        TurnToAsync(picked.FirstOrDefault() ?? HelpFormatter.OverviewKey, page: 0);

    [ComponentInteraction($"{ComponentIds.HelpPageButtonId}:*:*", ignoreGroupNames: true)]
    public Task PageTurnedAsync(string key, string page) =>
        TurnToAsync(key, int.TryParse(page, out var number) ? number : 0);

    private async Task ShowAsync(string? feature, bool isPublic)
    {
        try
        {
            var message = await RenderAsync(feature ?? HelpFormatter.OverviewKey, page: 0).ConfigureAwait(false);
            await RespondAsync(embed: message.Embed, components: message.Components, ephemeral: !isPublic,
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogHelpFailed(logger, ex);
            await TryRespondAsync().ConfigureAwait(false);
        }
    }

    private async Task TurnToAsync(string key, int page)
    {
        try
        {
            var message = await RenderAsync(key, page).ConfigureAwait(false);
            var interaction = (IComponentInteraction)Context.Interaction;

            if (interaction.Message.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
            {
                await interaction.UpdateAsync(properties =>
                {
                    properties.Embed = message.Embed;
                    properties.Components = message.Components;
                }).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(embed: message.Embed, components: message.Components, ephemeral: true,
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogHelpFailed(logger, ex);
            await TryRespondAsync().ConfigureAwait(false);
        }
    }

    private async Task<HelpMessage> RenderAsync(string key, int page)
    {
        var member = Context.User as IGuildUser
            ?? throw new InvalidOperationException("/help is only offered in the server.");

        var features = manual.FeaturesFor(member);
        var ids = await manual.CommandIdsAsync(Context.Guild).ConfigureAwait(false);

        return key == HelpFormatter.OverviewKey
            ? HelpFormatter.Overview(features, ids)
            : HelpFormatter.Feature(features, key, page, ids);
    }

    private async Task TryRespondAsync()
    {
        try
        {
            if (!Context.Interaction.HasResponded)
            {
                await RespondAsync("The manual could not be shown just now. Please try again later.", ephemeral: true)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogHelpFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to show the manual")]
    static partial void LogHelpFailed(ILogger logger, Exception ex);
}

/// <summary>Suggests the features the member may see, by title; the value is the feature's key.</summary>
public sealed class HelpFeatureAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        if (context.User is not IGuildUser member || services.GetService(typeof(IBotManual)) is not IBotManual manual)
        {
            return Task.FromResult(AutocompletionResult.FromSuccess());
        }

        var input = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;
        var suggestions = manual.FeaturesFor(member)
            .Where(f => f.Title.Contains(input, StringComparison.OrdinalIgnoreCase) || f.Key.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(f => new AutocompleteResult($"{f.Emoji} {f.Title}", f.Key))
            .Take(25);

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}
