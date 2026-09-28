using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges.Configuration;
using UseCases.UseCases.CountryChallenges.Rendering;

namespace UseCases.UseCases.CountryChallenges;

/// <summary>Posts the leaderboard of the current season, if it is due on the date and not posted yet.</summary>
public sealed record PostCountryChallengeLeaderboardCommand(CountryChallengePlan Plan, DateOnly Date)
    : ICommand<CountryChallengeLeaderboardOutcome>;

public sealed partial class PostCountryChallengeLeaderboardHandler(
    ICountryChallengeRepository repository,
    IDiscordMessageAccess discordMessageAccess,
    ISender mediator,
    IUnitOfWork unitOfWork,
    ILogger<PostCountryChallengeLeaderboardHandler> logger)
    : IRequestHandler<PostCountryChallengeLeaderboardCommand, CountryChallengeLeaderboardOutcome>
{
    public async Task<CountryChallengeLeaderboardOutcome> Handle(
        PostCountryChallengeLeaderboardCommand request,
        CancellationToken cancellationToken)
    {
        var plan = request.Plan;
        var leaderboard = plan.Leaderboard;

        if (!leaderboard.IsDueOn(request.Date))
        {
            return new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.NotDue);
        }

        if (await repository.IsLeaderboardPostedOnAsync(request.Date, cancellationToken).ConfigureAwait(false))
        {
            return new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.AlreadyPosted);
        }

        var awards = await repository.ReadAwardsAsync(leaderboard.Season, cancellationToken).ConfigureAwait(false);
        var standings = LeaderboardRanking.Rank(awards);
        if (standings.Count == 0)
        {
            // An empty leaderboard is noise; the first one appears once someone has points.
            return new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.Empty);
        }

        var message = CountryChallengeMessages.Leaderboard(plan, request.Date, standings);

        // Recorded before posting, so a second run the same day cannot post it again.
        var record = CountryChallengeLeaderboardPost.Create(request.Date, leaderboard.Season, DateTimeOffset.UtcNow);
        repository.AddLeaderboardPost(record);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await discordMessageAccess
                .SendMessageAsync(message.Content, leaderboard.ChannelId, message.Mentions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogNotPublished(logger, ex, leaderboard.ChannelId);

            // Not posted after all, so a manual run the same day may try again.
            repository.RemoveLeaderboardPost(record);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.Failed);
        }

        var assignment = CountryChallengeRoleAllocation.ForLeaderboard(leaderboard.RoleIds, standings);
        if (assignment is not null)
        {
            try
            {
                await mediator
                    .Send(new DistributeCountryChallengeRolesCommand(assignment), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRolesNotDistributed(logger, ex);
            }
        }

        return new CountryChallengeLeaderboardOutcome(CountryChallengeLeaderboardStatus.Posted);
    }

    [LoggerMessage(LogLevel.Error, "Could not post the country challenge leaderboard to channel {channelId}.")]
    static partial void LogNotPublished(ILogger<PostCountryChallengeLeaderboardHandler> logger, Exception exception, ulong channelId);

    [LoggerMessage(LogLevel.Error, "Could not hand out the country challenge leaderboard roles.")]
    static partial void LogRolesNotDistributed(ILogger<PostCountryChallengeLeaderboardHandler> logger, Exception exception);
}
