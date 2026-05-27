using Entities;
using UseCases.Abstractions;
using UseCases.OutputPorts;
using Utilities;

namespace UseCases.UseCases.Strikes;

public sealed record UnrevokeStrikeCommand(Guid StrikeId) : ICommand<Result<ClubMemberStrike>>;

public sealed class UnrevokeStrikeHandler(IStrikesRepository strikes)
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
        return strike;
    }
}
