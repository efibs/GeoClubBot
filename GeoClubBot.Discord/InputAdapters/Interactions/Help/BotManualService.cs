using Configuration;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>The manual for one member, and the command ids that make its mentions clickable.</summary>
public interface IBotManual
{
    /// <summary>The features and commands <paramref name="member"/> may see, in order.</summary>
    IReadOnlyList<ManualFeature> FeaturesFor(IGuildUser member);

    /// <summary>Top-level command name → id in <paramref name="guild"/>; empty when they cannot be read.</summary>
    Task<IReadOnlyDictionary<string, ulong>> CommandIdsAsync(IGuild guild);
}

/// <summary>
/// Builds the <see cref="BotManual"/> once from the loaded modules, and reads which features are switched
/// on from their settings.
/// </summary>
public sealed partial class BotManualService(
    InteractionService interactionService,
    IOptions<AiConfiguration> ai,
    IOptions<AiQuizConfiguration> aiQuiz,
    IOptions<AiRoundReviewConfiguration> aiRoundReview,
    IOptions<AiFeedbackConfiguration> aiFeedback,
    IOptions<CountryChallengesConfiguration> countryChallenges,
    IOptions<ClubChatForwardingConfiguration> clubChatForwarding,
    IOptions<SelfRolesConfiguration> selfRoles,
    IOptions<MissionBoardNotificationsConfiguration> missionBoardNotifications,
    IOptions<RandomDrawsConfiguration> randomDraws,
    ILogger<BotManualService> logger) : IBotManual
{
    public IReadOnlyList<ManualFeature> FeaturesFor(IGuildUser member) =>
        Manual().For(IsOn, permission => member.GuildPermissions.Has(permission));

    public async Task<IReadOnlyDictionary<string, ulong>> CommandIdsAsync(IGuild guild)
    {
        if (_commandIds is { } known)
        {
            return known;
        }

        try
        {
            // Ids stay the same while the names do, so the first read serves until restart.
            var commands = await guild.GetApplicationCommandsAsync().ConfigureAwait(false);
            var ids = commands
                .Where(command => command.Type == ApplicationCommandType.Slash)
                .GroupBy(command => command.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

            return _commandIds = ids;
        }
        catch (Exception ex)
        {
            LogCommandIdsUnreadable(logger, ex);
            return new Dictionary<string, ulong>();
        }
    }

    public bool IsOn(HelpToggle toggle) => toggle switch
    {
        HelpToggle.Always => true,
        HelpToggle.Ai => ai.Value.Active,
        HelpToggle.AiQuiz => ai.Value.Active && aiQuiz.Value.Enabled,
        HelpToggle.AiRoundReview => ai.Value.Active && aiRoundReview.Value.Enabled,
        HelpToggle.AiFeedback => ai.Value.Active && aiFeedback.Value.Enabled,
        HelpToggle.CountryChallenges => countryChallenges.Value.Enabled,
        HelpToggle.ClubChatForwarding => clubChatForwarding.Value.Enabled,
        HelpToggle.SelfRoles => selfRoles.Value.Roles.Count > 0,
        HelpToggle.MissionBoardNotifications => missionBoardNotifications.Value.Enabled,
        HelpToggle.RandomDraws => randomDraws.Value.Enabled,
        _ => throw new ArgumentOutOfRangeException(nameof(toggle), toggle, null)
    };

    private BotManual Manual()
    {
        if (_manual is { } built)
        {
            return built;
        }

        var manual = BotManual.Build(interactionService.SlashCommands, interactionService.ContextCommands);

        // Modules load when the gateway is ready; until then there is nothing to keep.
        if (manual.Features.Count == 0)
        {
            return manual;
        }

        foreach (var problem in manual.Problems)
        {
            LogManualProblem(logger, problem);
        }

        return _manual = manual;
    }

    private BotManual? _manual;

    private IReadOnlyDictionary<string, ulong>? _commandIds;

    [LoggerMessage(LogLevel.Warning, "Bot manual: {Problem}")]
    static partial void LogManualProblem(ILogger logger, string problem);

    [LoggerMessage(LogLevel.Warning, "Could not read the server's command ids; /help shows commands without links")]
    static partial void LogCommandIdsUnreadable(ILogger logger, Exception ex);
}
