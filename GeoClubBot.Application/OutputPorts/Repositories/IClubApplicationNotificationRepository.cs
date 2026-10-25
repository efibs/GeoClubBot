using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubApplicationNotificationRepository
{
    /// <summary>The applicants of <paramref name="clubId"/> whose pending application was already announced.</summary>
    Task<HashSet<string>> ReadNotifiedUserIdsAsync(Guid clubId, CancellationToken cancellationToken = default);

    void Add(ClubApplicationNotification notification);

    /// <summary>Forgets the announcements of applications that are no longer pending.</summary>
    Task<int> DeleteAsync(Guid clubId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}
