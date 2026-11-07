using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IClubChatForwardRepository
{
    /// <summary>Every earlier forward of the Discord message, oldest first.</summary>
    Task<IReadOnlyList<ClubChatForward>> ReadByMessageAsync(ulong discordMessageId, CancellationToken cancellationToken = default);

    void Add(ClubChatForward forward);
}
