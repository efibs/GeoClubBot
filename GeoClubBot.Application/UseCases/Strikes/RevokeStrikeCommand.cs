using Entities;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record RevokeStrikeCommand(Guid StrikeId) : ICommand<Result<ClubMemberStrike>>;

public sealed partial class RevokeStrikeHandler(
    IStrikesRepository strikes,
    ILogger<RevokeStrikeHandler> logger)
    : MediatR.IRequestHandler<RevokeStrikeCommand, Result<ClubMemberStrike>>
{
    public async Task<Result<ClubMemberStrike>> Handle(RevokeStrikeCommand request, CancellationToken cancellationToken)
    {
        var strike = await strikes.ReadForUpdateByIdAsync(request.StrikeId, cancellationToken).ConfigureAwait(false);

        if (strike is null)
        {
            return Error.NotFound("strike.not_found", $"Strike with id {request.StrikeId} not found.");
        }

        strike.Revoke();
        LogStrikeRevoked(logger, strike.StrikeId, strike.UserId);
        return strike;
    }

    [LoggerMessage(LogLevel.Information, "Strike {StrikeId} revoked for member {UserId}.")]
    static partial void LogStrikeRevoked(ILogger<RevokeStrikeHandler> logger, Guid strikeId, string userId);
}
