using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record RevokeStrikeCommand(Guid StrikeId) : ICommand<ClubMemberStrike?>;

public sealed class RevokeStrikeHandler(IStrikesRepository strikes)
    : MediatR.IRequestHandler<RevokeStrikeCommand, ClubMemberStrike?>
{
    public async Task<ClubMemberStrike?> Handle(RevokeStrikeCommand request, CancellationToken cancellationToken)
    {
        var strike = await strikes.ReadForUpdateByIdAsync(request.StrikeId, cancellationToken).ConfigureAwait(false);

        if (strike is null)
        {
            return null;
        }

        strike.Revoke();
        return strike;
    }
}
