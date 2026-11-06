using Entities;

namespace UseCases.OutputPorts.Repositories;

public interface IMemberPrivateChannelCategoryRepository
{
    /// <summary>Every category the bot created for private channels, ordered by club and index.</summary>
    Task<List<MemberPrivateChannelCategory>> ReadAllAsync(CancellationToken cancellationToken = default);

    void Add(MemberPrivateChannelCategory category);

    Task DeleteAsync(ulong categoryId, CancellationToken cancellationToken = default);
}
