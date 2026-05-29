using Entities;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record UnrevokeStrikeCommand(Guid StrikeId) : ICommand<Result<ClubMemberStrike>>;

public sealed partial class UnrevokeStrikeHandler(
    IStrikesRepository strikes,
    ILogger<UnrevokeStrikeHandler> logger)
    : MediatR.IRequestHandler<UnrevokeStrikeCommand, Result<ClubMemberStrike>>
{
    public async Task<Result<ClubMemberStrike>> Handle(UnrevokeStrikeCommand request, CancellationToken cancellationToken)
    {
        var strike = await strikes.ReadForUpdateByIdAsync(request.StrikeId, cancellationToken).ConfigureAwait(false);

        if (strike is null)
        {
            return Error.NotFound("strike.not_found", $"Strike with id {request.StrikeId} not found.");
        }

        strike.Unrevoke();
        LogStrikeUnrevoked(logger, strike.StrikeId, strike.UserId);
        return strike;
    }

    [LoggerMessage(LogLevel.Information, "Strike {StrikeId} unrevoked for member {UserId}.")]
    static partial void LogStrikeUnrevoked(ILogger<UnrevokeStrikeHandler> logger, Guid strikeId, string userId);
}
