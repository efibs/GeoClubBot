using Entities;
using UseCases.OutputPorts.Discord;

namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// Where every channel of the server is right now, plus the bot's private-channel categories, read once
/// per operation and kept up to date as the operation moves, creates and deletes channels — so a
/// reconcile of fifty members counts each category correctly without re-reading the server.
/// </summary>
public sealed class PrivateChannelPlacement
{
    private readonly Dictionary<ulong, ulong?> _parentOf = new();
    private readonly Dictionary<ulong, IReadOnlyList<ChannelAccess>> _accessOf = new();
    private readonly HashSet<ulong> _discordCategories = [];
    private readonly List<MemberPrivateChannelCategory> _categories;

    public PrivateChannelPlacement(GuildChannelLayout layout, IEnumerable<MemberPrivateChannelCategory> categories)
    {
        EveryoneRoleId = layout.EveryoneRoleId;
        BotUserId = layout.BotUserId;

        foreach (var channel in layout.Channels)
        {
            _accessOf[channel.Id] = channel.Access;

            if (channel.IsCategory)
            {
                _discordCategories.Add(channel.Id);
            }
            else
            {
                _parentOf[channel.Id] = channel.ParentCategoryId;
            }
        }

        var all = categories.ToList();
        _categories = all.Where(c => _discordCategories.Contains(c.CategoryId)).ToList();
        Vanished = all.Where(c => !_discordCategories.Contains(c.CategoryId)).ToList();
    }

    public ulong EveryoneRoleId { get; }

    public ulong BotUserId { get; }

    /// <summary>Category rows whose Discord category was deleted by hand.</summary>
    public IReadOnlyList<MemberPrivateChannelCategory> Vanished { get; }

    public IReadOnlyList<MemberPrivateChannelCategory> Categories => _categories;

    public bool ChannelExists(ulong? channelId) => channelId is not null && _parentOf.ContainsKey(channelId.Value);

    public ulong? ParentOf(ulong channelId) => _parentOf.GetValueOrDefault(channelId);

    public IReadOnlyList<ChannelAccess> AccessOf(ulong channelId) =>
        _accessOf.GetValueOrDefault(channelId) ?? [];

    /// <summary>Channels inside a category, including any an admin put there by hand.</summary>
    public int CountIn(ulong categoryId) => _parentOf.Values.Count(p => p == categoryId);

    public IEnumerable<MemberPrivateChannelCategory> CategoriesOf(Guid clubId) =>
        _categories.Where(c => c.ClubId == clubId).OrderBy(c => c.Index);

    public bool IsCategoryOf(Guid clubId, ulong? categoryId) =>
        categoryId is not null && _categories.Any(c => c.ClubId == clubId && c.CategoryId == categoryId);

    public bool IsBotCategory(ulong? categoryId) =>
        categoryId is not null && _categories.Any(c => c.CategoryId == categoryId);

    /// <summary>The lowest index the club doesn't use yet, which names its next category.</summary>
    public int NextIndexOf(Guid clubId)
    {
        var used = _categories.Where(c => c.ClubId == clubId).Select(c => c.Index).ToHashSet();
        var index = 1;
        while (used.Contains(index))
        {
            index++;
        }

        return index;
    }

    public void Placed(ulong channelId, ulong categoryId) => _parentOf[channelId] = categoryId;

    public void AccessSet(ulong channelId, IReadOnlyList<ChannelAccess> access) => _accessOf[channelId] = access;

    public void Removed(ulong channelId)
    {
        _parentOf.Remove(channelId);
        _accessOf.Remove(channelId);
    }

    public void CategoryAdded(MemberPrivateChannelCategory category, IReadOnlyList<ChannelAccess> access)
    {
        _discordCategories.Add(category.CategoryId);
        _accessOf[category.CategoryId] = access;
        _categories.Add(category);
    }

    public void CategoryRemoved(ulong categoryId)
    {
        _discordCategories.Remove(categoryId);
        _accessOf.Remove(categoryId);
        _categories.RemoveAll(c => c.CategoryId == categoryId);
    }
}
