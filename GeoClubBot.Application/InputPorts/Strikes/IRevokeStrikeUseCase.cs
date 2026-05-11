using Entities;

namespace UseCases.InputPorts.Strikes;

public interface IRevokeStrikeUseCase
{
    Task<ClubMemberStrike?> RevokeStrikeAsync(Guid strikeId);
}