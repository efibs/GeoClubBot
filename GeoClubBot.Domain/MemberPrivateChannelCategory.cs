namespace Entities;

/// <summary>
/// A Discord category the bot created to hold the private channels of one club's members. Discord
/// caps a category at 50 channels, so a club gets a second one (<see cref="Index"/> 2, …) when its
/// first is full. Kept in the database rather than found by name, so admins may rename or reorder them.
/// </summary>
public class MemberPrivateChannelCategory : BaseEntity
{
    public ulong CategoryId { get; private set; }

    public Guid ClubId { get; private set; }

    /// <summary>1 for a club's first category, 2 for the overflow, and so on. Also its name's number.</summary>
    public int Index { get; private set; }

    public static MemberPrivateChannelCategory Create(ulong categoryId, Guid clubId, int index) =>
        new()
        {
            CategoryId = categoryId,
            ClubId = clubId,
            Index = index
        };

    private MemberPrivateChannelCategory()
    {
    }
}
