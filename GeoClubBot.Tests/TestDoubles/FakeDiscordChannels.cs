using Entities;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.Tests.TestDoubles;

/// <summary>
/// An in-memory Discord server for the private-channel tests: channels, categories, their parents and
/// overwrites, so a test can fill a category, read where a channel ended up and count what was written.
/// </summary>
public sealed class FakeDiscordChannels : IDiscordTextChannelAccess
{
    public const ulong EveryoneRoleId = 1;
    public const ulong BotUserId = 2;

    private readonly object _gate = new();
    private ulong _nextId = 10_000;

    public sealed class Channel
    {
        public required ulong Id { get; init; }
        public required string Name { get; set; }
        public ulong? Parent { get; set; }
        public bool IsCategory { get; init; }
        public IReadOnlyList<ChannelAccess> Access { get; set; } = [];
    }

    public Dictionary<ulong, Channel> Channels { get; } = new();

    /// <summary>Every mutating call, in order, e.g. "create-channel player1-private-channel".</summary>
    public List<string> Writes { get; } = [];

    public bool RefuseCreates { get; set; }

    public bool RefuseUpdates { get; set; }

    public Channel AddCategory(ulong id, string name = "category", IReadOnlyList<ChannelAccess>? access = null)
    {
        lock (_gate)
        {
            var category = new Channel { Id = id, Name = name, IsCategory = true, Access = access ?? [] };
            Channels[id] = category;
            return category;
        }
    }

    public Channel AddChannel(ulong id, ulong? parent, string name = "channel", IReadOnlyList<ChannelAccess>? access = null)
    {
        lock (_gate)
        {
            var channel = new Channel { Id = id, Name = name, Parent = parent, Access = access ?? [] };
            Channels[id] = channel;
            return channel;
        }
    }

    public IReadOnlyList<Channel> CategoriesNamed(string name) =>
        Channels.Values.Where(c => c.IsCategory && c.Name == name).ToList();

    public int CountIn(ulong categoryId) => Channels.Values.Count(c => !c.IsCategory && c.Parent == categoryId);

    public Task<ulong?> CreatePrivateTextChannelAsync(ulong categoryId, string name, string description,
        IReadOnlyCollection<ChannelAccess> access, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (RefuseCreates)
            {
                return Task.FromResult<ulong?>(null);
            }

            Writes.Add($"create-channel {name}");
            var id = ++_nextId;
            Channels[id] = new Channel { Id = id, Name = name, Parent = categoryId, Access = access.ToList() };
            return Task.FromResult<ulong?>(id);
        }
    }

    public Task<ulong?> CreateCategoryAsync(string name, IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (RefuseCreates)
            {
                return Task.FromResult<ulong?>(null);
            }

            Writes.Add($"create-category {name}");
            var id = ++_nextId;
            Channels[id] = new Channel { Id = id, Name = name, IsCategory = true, Access = access.ToList() };
            return Task.FromResult<ulong?>(id);
        }
    }

    public Task<GuildChannelLayout> ReadChannelLayoutAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var infos = Channels.Values
                .Select(c => new GuildChannelInfo(c.Id, c.Parent, c.IsCategory, c.Access.ToList()))
                .ToList();
            return Task.FromResult(new GuildChannelLayout(EveryoneRoleId, BotUserId, infos));
        }
    }

    public Task<bool> SetChannelAccessAsync(ulong channelId, IReadOnlyCollection<ChannelAccess> access,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Channels.TryGetValue(channelId, out var channel))
            {
                return Task.FromResult(false);
            }

            Writes.Add($"set-access {channelId}");
            channel.Access = access.ToList();
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateTextChannelAsync(TextChannel newTextChannel, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (RefuseUpdates || !Channels.TryGetValue(newTextChannel.Id, out var channel) || channel.IsCategory)
            {
                return Task.FromResult(false);
            }

            Writes.Add($"update {newTextChannel.Id}");
            if (!string.IsNullOrWhiteSpace(newTextChannel.Name))
            {
                channel.Name = newTextChannel.Name;
            }

            if (newTextChannel.CategoryId is not null)
            {
                channel.Parent = newTextChannel.CategoryId;
            }

            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteTextChannelAsync(ulong textChannelId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Writes.Add($"delete-channel {textChannelId}");
            return Task.FromResult(Channels.Remove(textChannelId));
        }
    }

    public Task<bool> DeleteCategoryAsync(ulong categoryId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Writes.Add($"delete-category {categoryId}");
            return Task.FromResult(Channels.Remove(categoryId));
        }
    }

    public Task<ulong?> ReadLastMessageOfUserAsync(ulong userId, ulong channelId, int numMessageSearchlimit,
        CancellationToken cancellationToken = default) => Task.FromResult<ulong?>(null);
}

/// <summary>An in-memory category table for the unit tests.</summary>
public sealed class FakeMemberPrivateChannelCategories : UseCases.OutputPorts.Repositories.IMemberPrivateChannelCategoryRepository
{
    public List<MemberPrivateChannelCategory> Rows { get; } = [];

    public Task<List<MemberPrivateChannelCategory>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.OrderBy(r => r.ClubId).ThenBy(r => r.Index).ToList());

    public void Add(MemberPrivateChannelCategory category) => Rows.Add(category);

    public Task DeleteAsync(ulong categoryId, CancellationToken cancellationToken = default)
    {
        Rows.RemoveAll(r => r.CategoryId == categoryId);
        return Task.CompletedTask;
    }
}
