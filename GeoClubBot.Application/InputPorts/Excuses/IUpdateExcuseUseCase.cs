using Entities;

namespace UseCases.InputPorts.Excuses;

public interface IUpdateExcuseUseCase
{
    Task<ClubMemberExcuse?> UpdateExcuseAsync(Guid excuseId, DateTimeOffset from, DateTimeOffset to);
}