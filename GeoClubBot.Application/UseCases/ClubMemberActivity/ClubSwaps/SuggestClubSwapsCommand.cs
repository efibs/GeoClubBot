using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;

namespace UseCases.UseCases.ClubMemberActivity.ClubSwaps;

/// <summary>
/// Posts which second-club members should move into the main club, after the weekly check has
/// recorded this week's rule XP. Suggestions only: GeoGuessr has no API to move members.
/// </summary>
/// <param name="Statuses">This week's statuses of every club, to find main-club members out of strikes.</param>
public sealed record SuggestClubSwapsCommand(List<ClubMemberActivityStatus> Statuses) : ICommand;

public sealed partial class SuggestClubSwapsHandler(
    ISender mediator,
    IClubRepository clubs,
    IClubMemberRepository clubMembers,
    IDiscordMessageAccess discordMessageAccess,
    IOptions<SwapSuggestionsConfiguration> config,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig,
    IOptions<GeoGuessrConfiguration> geoGuessrConfig,
    ILogger<SuggestClubSwapsHandler> logger) : IRequestHandler<SuggestClubSwapsCommand, Unit>
{
    public async Task<Unit> Handle(SuggestClubSwapsCommand request, CancellationToken cancellationToken)
    {
        var options = config.Value;
        if (!options.Enabled)
        {
            return Unit.Value;
        }

        var mainClubId = geoGuessrConfig.Value.MainClub.ClubId;
        var secondClubIds = geoGuessrConfig.Value.Clubs.Where(c => !c.IsMain).Select(c => c.ClubId).ToList();
        if (secondClubIds.Count == 0)
        {
            LogNoSecondClub(logger);
            return Unit.Value;
        }

        var mainAverages = await mediator
            .Send(new CalculateAverageXpQuery(mainClubId, options.HistoryDepth), cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<SwapCandidate>();
        foreach (var clubId in secondClubIds)
        {
            var clubName = (await clubs.ReadClubByIdAsync(clubId, cancellationToken).ConfigureAwait(false))?.Name
                           ?? clubId.ToString();
            var averages = await mediator
                .Send(new CalculateAverageXpQuery(clubId, options.HistoryDepth), cancellationToken)
                .ConfigureAwait(false);
            candidates.AddRange(averages
                .Where(a => a.UserId is not null)
                .Select(a => new SwapCandidate(a.UserId!, a.Nickname, a.AverageXp, clubName)));
        }

        var mainMembers = await clubMembers
            .ReadClubMembersByClubIdAsync(mainClubId, cancellationToken)
            .ConfigureAwait(false);
        var mainMemberIds = mainMembers.Select(m => m.UserId).ToHashSet();
        var mainAveragesByUser = mainAverages
            .Where(a => a.UserId is not null)
            .ToDictionary(a => a.UserId!);

        // A kicked member may be too new to have an average; they are listed with this week's XP.
        var kicked = request.Statuses
            .Where(s => s.IsOutOfStrikes && mainMemberIds.Contains(s.UserId))
            .Select(s => mainAveragesByUser.GetValueOrDefault(s.UserId)
                         ?? new ClubMemberAverageXp(s.Nickname, s.RankingXp, DateTimeOffset.MaxValue, s.UserId))
            .ToList();

        var suggestions = ClubSwapPlanner.Plan(
            mainAverages,
            candidates,
            kicked,
            mainMembers.Count,
            new SwapPlanOptions(options.BufferXp, options.MaxClubSize, options.SuggestReplacementsForKicks, options.SuggestPromotions));

        var mainClubName = (await clubs.ReadClubByIdAsync(mainClubId, cancellationToken).ConfigureAwait(false))?.Name
                           ?? mainClubId.ToString();
        var message = ClubSwapSuggestionMessage.Render(mainClubName, suggestions, options.HistoryDepth, options.BufferXp);

        await discordMessageAccess
            .SendMessageAsync(message, options.TextChannelId ?? activityCheckerConfig.Value.TextChannelId, cancellationToken)
            .ConfigureAwait(false);

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Warning, "Swap suggestions are enabled, but no second club is configured.")]
    static partial void LogNoSecondClub(ILogger<SuggestClubSwapsHandler> logger);
}
