using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record UnrevokeStrikeCommand(Guid StrikeId) : ICommand<ClubMemberStrike?>;

public sealed class UnrevokeStrikeHandler(IStrikesRepository strikes)
    : MediatR.IRequestHandler<UnrevokeStrikeCommand, ClubMemberStrike?>
{
    public async Task<ClubMemberStrike?> Handle(UnrevokeStrikeCommand request, CancellationToken cancellationToken)
    {
        var strike = await strikes.ReadForUpdateByIdAsync(request.StrikeId, cancellationToken).ConfigureAwait(false);

        if (strike is null)
        {
            return null;
        }

        strike.Unrevoke();
        return strike;
    }
}
