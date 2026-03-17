using Entities;

namespace UseCases.InputPorts.ClubMemberActivity;

public interface ICheckGeoGuessrPlayerActivityUseCase
{
    Task<List<ClubMemberActivityStatus>> CheckPlayerActivityAsync(Guid clubId);
}