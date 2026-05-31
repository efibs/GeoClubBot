using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using Utilities;

namespace UseCases.UseCases.RankedSystem;

public sealed record GetUserRankedProgressQuery(ulong DiscordUserId) : IQuery<Result<RankedProgressResponseDto>>;
public sealed record GetUserRankedPeakRatingQuery(ulong DiscordUserId) : IQuery<Result<RankedPeakRatingResponseDto>>;

public sealed class RankedSystemQueriesHandler(IGeoGuessrUserRepository users,
    IGeoGuessrUserRankedSystemReader reader)
    : IRequestHandler<GetUserRankedProgressQuery, Result<RankedProgressResponseDto>>,
      IRequestHandler<GetUserRankedPeakRatingQuery, Result<RankedPeakRatingResponseDto>>
{
    private static readonly Error NotLinked =
        Error.NotFound("ranked_system.not_linked", "No linked account exists for the requested user.");
    private static readonly Error ProgressNotFound =
        Error.NotFound("ranked_system.progress_not_found", "Ranked progress does not exist for that user.");
    private static readonly Error PeakRatingNotFound =
        Error.NotFound("ranked_system.peak_rating_not_found", "Peak rating does not exist for that user.");

    public async Task<Result<RankedProgressResponseDto>> Handle(GetUserRankedProgressQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return NotLinked;
        }

        var rankedProgress = await reader.ReadRankedProgressOfUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        return rankedProgress is not null ? rankedProgress : ProgressNotFound;
    }

    public async Task<Result<RankedPeakRatingResponseDto>> Handle(GetUserRankedPeakRatingQuery request, CancellationToken cancellationToken)
    {
        var user = await users.ReadUserByDiscordUserIdAsync(request.DiscordUserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return NotLinked;
        }

        RankedPeakRatingResponseDto? peakRating;

        try
        {
            peakRating = await reader
                .ReadRankedPeakRatingOfUserAsync(user.UserId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return PeakRatingNotFound;
        }

        return peakRating is not null ? peakRating : PeakRatingNotFound;
    }
}
