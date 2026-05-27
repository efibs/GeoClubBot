using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record RevokeStrikeCommand(Guid StrikeId) : ICommand<Result<ClubMemberStrike>>;

public sealed class RevokeStrikeHandler(IStrikesRepository strikes)
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
        return strike;
    }
}
