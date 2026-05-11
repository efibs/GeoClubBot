using Entities;

namespace UseCases.InputPorts.Strikes;

public interface IUnrevokeStrikeUseCase
{
    Task<ClubMemberStrike?> UnrevokeStrikeAsync(Guid strikeId);
}