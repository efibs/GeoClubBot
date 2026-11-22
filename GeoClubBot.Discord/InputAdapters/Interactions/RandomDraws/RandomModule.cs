using Configuration;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using GeoClubBot.Discord.InputAdapters.Interactions.Base;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.UseCases.RandomDraws;

namespace GeoClubBot.Discord.InputAdapters.Interactions.RandomDraws;

/// <summary>Where <c>/random teams</c> takes its people from.</summary>
public enum TeamSource
{
    [ChoiceDisplay("A voice channel")]
    Voice,

    [ChoiceDisplay("A role")]
    Role,

    [ChoiceDisplay("A club")]
    Club,

    [ChoiceDisplay("A list I type")]
    List
}

/// <summary>
/// <c>/random</c> (#2): draws of countries, numbers, coins, dice and list items for everyone; of club
/// members, people in a voice channel or with roles, and random teams for members who may manage
/// messages. Results are public unless <c>private</c> is set, never repeat an item within one draw, and
/// never ping anyone they mention.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
[HelpFeature("random", Emoji = "🎲", Title = "Random draws", Order = 85, ShownWhen = HelpToggle.RandomDraws,
    Summary = "Draw countries, numbers, dice, list items, club members, people in a voice channel or teams.")]
[HelpSection(1, "How draws work", """
    Everyone sees the result unless you set `private`. A draw of several never takes the same item twice, and the footer says how many there were to draw from. People are shown, never pinged; bots and anyone deafened in a voice channel are never drawn.
    """)]
[Group("random", "Draw something at random")]
public partial class RandomModule(
    ISender mediator,
    ILogger<RandomModule> logger,
    IOptions<RandomDrawsConfiguration> options)
    : ClubBotInteractionModule(mediator, logger)
{
    private const int MaxDice = 100;
    private const int MaxSides = 1000;
    private const long MaxNumber = 1_000_000_000;
    private const int MaxListItems = 100;

    private const string CountSummary = "How many to draw";
    private const string PrivateSummary = "Show the result only to you";
    private const string ExcludeMeSummary = "Leave yourself out";
    private const string ExcludeSummary = "Leave this person out";
    private const string FailureMessage = "Could not draw. Please try again later. If the issue persists, contact an admin.";

    private readonly RandomDrawsConfiguration _config = options.Value;

    [SlashCommand("country", "Draw countries, optionally only those with Street View")]
    [HelpDetail("Each country links to its PlonkIt guide when there is one.")]
    public Task CountryAsync(
        [Summary("count", CountSummary)] int count = 1,
        [Summary("coverage-only", "Only countries with official Street View coverage")] bool coverageOnly = false,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, async () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return problem;
            }

            var pool = await Mediator.Send(new ReadDrawableCountriesQuery(coverageOnly)).ConfigureAwait(false);
            var drawn = RandomDraw.Pick(pool, count, System.Random.Shared);
            return new DrawCard(
                drawn.Count == 1 ? "🌍 Random country" : "🌍 Random countries",
                drawn.Select(RandomDrawFormatter.Country).ToList(),
                RandomDrawFormatter.Footer(drawn.Count, pool.Count, coverageOnly ? "countries with Street View" : "all countries", CallerName),
                RandomDrawFormatter.ShortPoolNote(count, pool.Count));
        });

    [SlashCommand("number", "Draw whole numbers from a range")]
    public Task NumberAsync(
        [Summary("min", "The smallest number")] long min = 1,
        [Summary("max", "The largest number")] long max = 100,
        [Summary("count", CountSummary)] int count = 1,
        [Summary("allow-repeats", "Let the same number come up more than once")] bool allowRepeats = false,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return Task.FromResult<object>(problem);
            }

            if (min > max)
            {
                (min, max) = (max, min);
            }

            if (min < -MaxNumber || max > MaxNumber)
            {
                return Task.FromResult<object>($"Numbers must lie between -{MaxNumber:N0} and {MaxNumber:N0}.");
            }

            var rangeSize = max - min + 1;
            if (!allowRepeats && rangeSize < count)
            {
                return Task.FromResult<object>(
                    $"There are only {rangeSize} numbers from {min} to {max}; draw fewer or allow repeats.");
            }

            var drawn = RandomDraw.Numbers(min, max, count, allowRepeats, System.Random.Shared);
            return Task.FromResult<object>(new DrawCard(
                drawn.Count == 1 ? "🔢 Random number" : "🔢 Random numbers",
                drawn.Select(number => $"**{number}**").ToList(),
                $"{drawn.Count} from {min} to {max}{(allowRepeats ? " with repeats" : string.Empty)} · drawn by {CallerName}"));
        });

    [SlashCommand("coin", "Flip coins")]
    public Task CoinAsync(
        [Summary("count", "How many coins to flip")] int count = 1,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return Task.FromResult<object>(problem);
            }

            var flips = RandomDraw.Coins(count, System.Random.Shared);
            var heads = flips.Count(flip => flip);
            return Task.FromResult<object>(new DrawCard(
                "🪙 Coin flip",
                flips.Select(flip => flip ? "**Heads**" : "**Tails**").ToList(),
                $"{count} {(count == 1 ? "coin" : "coins")} · flipped by {CallerName}",
                count > 1 ? $"Heads {heads} · Tails {count - heads}" : null));
        });

    [SlashCommand("dice", "Roll dice")]
    [HelpDetail("Up to 100 dice of up to 1000 sides; several dice also show their sum.")]
    public Task DiceAsync(
        [Summary("sides", "Sides of each die")] int sides = 6,
        [Summary("count", "How many dice")] int count = 1,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (sides is < 2 or > MaxSides)
            {
                return Task.FromResult<object>($"A die has 2 to {MaxSides} sides.");
            }

            if (count is < 1 or > MaxDice)
            {
                return Task.FromResult<object>($"Roll 1 to {MaxDice} dice.");
            }

            var throws = RandomDraw.Dice(sides, count, System.Random.Shared);
            var items = count == 1 ? [$"**{throws[0]}**"] : new List<string> { string.Join(" · ", throws.Select(t => $"**{t}**")) };
            return Task.FromResult<object>(new DrawCard(
                "🎲 Dice roll",
                items,
                $"{count}d{sides} · rolled by {CallerName}",
                count > 1 ? $"Sum **{throws.Sum()}**" : null));
        });

    [SlashCommand("pick", "Draw items from a list you type")]
    [HelpDetail("Separate the items with commas or semicolons. An item typed twice is twice as likely.")]
    public Task PickAsync(
        [Summary("items", "The items, separated by commas")] string items,
        [Summary("count", CountSummary)] int count = 1,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return Task.FromResult<object>(problem);
            }

            if (ReadList(items) is not { } pool)
            {
                return Task.FromResult<object>(ListProblem(items));
            }

            var drawn = RandomDraw.Pick(pool, count, System.Random.Shared);
            return Task.FromResult<object>(new DrawCard(
                "🎯 Random pick",
                drawn.Select(RandomDrawFormatter.Escape).ToList(),
                RandomDrawFormatter.Footer(drawn.Count, pool.Count, "your list", CallerName),
                RandomDrawFormatter.ShortPoolNote(count, pool.Count)));
        });

    [SlashCommand("shuffle", "Put a list you type into a random order")]
    [HelpDetail("Separate the items with commas or semicolons, e.g. a play order.")]
    public Task ShuffleAsync(
        [Summary("items", "The items, separated by commas")] string items,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (ReadList(items) is not { } pool)
            {
                return Task.FromResult<object>(ListProblem(items));
            }

            var order = RandomDraw.Shuffle(pool, System.Random.Shared);
            return Task.FromResult<object>(new DrawCard(
                "🔀 Shuffled",
                order.Select(RandomDrawFormatter.Escape).ToList(),
                $"{order.Count} items · shuffled by {CallerName}"));
        });

    [SlashCommand("member", "Draw members of a club, or of all our clubs")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [HelpDetail("Shows each member's GeoGuessr name and, when linked, their Discord account.")]
    public Task MemberAsync(
        [Autocomplete(typeof(DrawClubAutocompleteHandler))][Summary("club", "The club, or all our clubs; the main club if left out")] string? club = null,
        [Summary("count", CountSummary)] int count = 1,
        [Summary("exclude-me", ExcludeMeSummary)] bool excludeMe = false,
        [Summary("exclude", ExcludeSummary)] IUser? exclude = null,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, async () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return problem;
            }

            var members = await ReadClubMembersAsync(club, excludeMe, exclude).ConfigureAwait(false);
            if (members is not (var poolName, var pool, var allClubs))
            {
                return ClubOption.UnknownClubMessage;
            }

            var drawn = RandomDraw.Pick(pool, count, System.Random.Shared);
            return new DrawCard(
                drawn.Count == 1 ? "👤 Random member" : "👥 Random members",
                drawn.Select(member => RandomDrawFormatter.Member(member, allClubs)).ToList(),
                RandomDrawFormatter.Footer(drawn.Count, pool.Count, poolName, CallerName),
                pool.Count == 0 ? "Nobody to draw from." : RandomDrawFormatter.ShortPoolNote(count, pool.Count));
        });

    [SlashCommand("voice", "Draw people in a voice channel")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [HelpDetail("Takes the voice channel you are in when none is picked. Deafened people are left out.")]
    public Task VoiceAsync(
        [Summary("channel", "The voice channel; yours if left out")][ChannelTypes(ChannelType.Voice, ChannelType.Stage)] IVoiceChannel? channel = null,
        [Summary("count", CountSummary)] int count = 1,
        [Summary("exclude-me", ExcludeMeSummary)] bool excludeMe = false,
        [Summary("exclude", ExcludeSummary)] IUser? exclude = null,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return Task.FromResult<object>(problem);
            }

            if (ReadVoice(channel, excludeMe, exclude) is not (var voiceChannel, var pool))
            {
                return Task.FromResult<object>("Join a voice channel or pick one.");
            }

            return Task.FromResult<object>(PeopleCard(
                count == 1 ? "🔊 Random person in voice" : "🔊 Random people in voice",
                pool, count, $"🔊 {voiceChannel.Name}"));
        });

    [SlashCommand("role", "Draw people with one or all of up to five roles")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public Task RoleAsync(
        [Summary("role", "The role")] IRole role,
        [Summary("role2", "Another role")] IRole? role2 = null,
        [Summary("role3", "Another role")] IRole? role3 = null,
        [Summary("role4", "Another role")] IRole? role4 = null,
        [Summary("role5", "Another role")] IRole? role5 = null,
        [Summary("match", "With several roles: anyone with one of them, or only with all")] RoleMatch match = RoleMatch.Any,
        [Summary("count", CountSummary)] int count = 1,
        [Summary("exclude-me", ExcludeMeSummary)] bool excludeMe = false,
        [Summary("exclude", ExcludeSummary)] IUser? exclude = null,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, () =>
        {
            if (CheckCount(count) is { } problem)
            {
                return Task.FromResult<object>(problem);
            }

            IRole[] roles = [.. new[] { role, role2, role3, role4, role5 }.OfType<IRole>().DistinctBy(r => r.Id)];
            var pool = ReadRolePeople(roles, match, excludeMe, exclude);
            var roleNames = string.Join(match == RoleMatch.All ? " & " : " or ", roles.Select(r => $"@{r.Name}"));

            return Task.FromResult<object>(PeopleCard(
                count == 1 ? "🎭 Random person with the role" : "🎭 Random people with the role",
                pool, count, roleNames));
        });

    [SlashCommand("teams", "Split people or a list into random teams")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [HelpDetail("Pick where the people come from and fill in the matching option: `channel` (yours if left out), `role`, `club` or `items`. Team sizes differ by at most one.")]
    public Task TeamsAsync(
        [Summary("teams", "How many teams")] int teams,
        [Summary("from", "Where the people come from")] TeamSource from,
        [Summary("channel", "With from: voice — the voice channel; yours if left out")][ChannelTypes(ChannelType.Voice, ChannelType.Stage)] IVoiceChannel? channel = null,
        [Summary("role", "With from: role — the role")] IRole? role = null,
        [Autocomplete(typeof(DrawClubAutocompleteHandler))][Summary("club", "With from: club — the club or all our clubs; the main club if left out")] string? club = null,
        [Summary("items", "With from: list — the names, separated by commas")] string? items = null,
        [Summary("exclude-me", ExcludeMeSummary)] bool excludeMe = false,
        [Summary("exclude", ExcludeSummary)] IUser? exclude = null,
        [Summary("private", PrivateSummary)] bool hidden = false) =>
        DrawAsync(hidden, async () =>
        {
            if (teams < 2 || teams > _config.MaxCount)
            {
                return $"Make 2 to {_config.MaxCount} teams.";
            }

            List<string> people;
            string poolName;
            switch (from)
            {
                case TeamSource.Voice:
                    if (ReadVoice(channel, excludeMe, exclude) is not (var voiceChannel, var inVoice))
                    {
                        return "Join a voice channel or pick one.";
                    }

                    people = inVoice.Select(RandomDrawFormatter.Person).ToList();
                    poolName = $"🔊 {voiceChannel.Name}";
                    break;
                case TeamSource.Role:
                    if (role is null)
                    {
                        return "Pick the `role` to split into teams.";
                    }

                    people = ReadRolePeople([role], RoleMatch.Any, excludeMe, exclude).Select(RandomDrawFormatter.Person).ToList();
                    poolName = $"@{role.Name}";
                    break;
                case TeamSource.Club:
                    if (await ReadClubMembersAsync(club, excludeMe, exclude).ConfigureAwait(false) is not (var clubName, var members, var allClubs))
                    {
                        return ClubOption.UnknownClubMessage;
                    }

                    people = members.Select(member => RandomDrawFormatter.Member(member, allClubs)).ToList();
                    poolName = clubName;
                    break;
                default:
                    if (ReadList(items) is not { } list)
                    {
                        return ListProblem(items);
                    }

                    people = list.Select(RandomDrawFormatter.Escape).ToList();
                    poolName = "your list";
                    break;
            }

            if (people.Count < teams)
            {
                return $"There are only {people.Count} to split into {teams} teams.";
            }

            var split = RandomDraw.Teams(people, teams, System.Random.Shared);
            return new DrawCard(
                "🏁 Random teams",
                [],
                $"{people.Count} into {teams} teams · {poolName} · drawn by {CallerName}",
                Teams: split.Select((team, index) => new DrawTeam($"Team {index + 1}", team)).ToList());
        });

    /// <summary>
    /// Runs a draw: <paramref name="draw"/> returns the card to show, or a sentence saying why it could not
    /// draw, which only the caller sees.
    /// </summary>
    private async Task DrawAsync(bool hidden, Func<Task<object>> draw)
    {
        if (!_config.Enabled)
        {
            await RespondAsync("Random draws are switched off.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ExecuteAsync(async _ =>
        {
            var outcome = await draw().ConfigureAwait(false);
            var embed = outcome is DrawCard card ? RandomDrawFormatter.Render(card) : null;

            if (embed is null)
            {
                var problem = outcome as string ?? "That is too much to show in one message; draw fewer or shorter items.";
                await FollowupAsync(problem, ephemeral: true, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: embed, ephemeral: hidden, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }, ephemeral: hidden, failureMessage: FailureMessage).ConfigureAwait(false);
    }

    private DrawCard PeopleCard(string title, IReadOnlyList<ulong> pool, int count, string poolName)
    {
        var drawn = RandomDraw.Pick(pool, count, System.Random.Shared);
        return new DrawCard(
            title,
            drawn.Select(RandomDrawFormatter.Person).ToList(),
            RandomDrawFormatter.Footer(drawn.Count, pool.Count, poolName, CallerName),
            pool.Count == 0 ? "Nobody to draw from." : RandomDrawFormatter.ShortPoolNote(count, pool.Count));
    }

    private string? CheckCount(int count) =>
        count < 1 || count > _config.MaxCount ? $"Draw 1 to {_config.MaxCount} at a time." : null;

    private static List<string>? ReadList(string? items)
    {
        var list = RandomDraw.ParseList(items);
        return list.Count is > 0 and <= MaxListItems ? list : null;
    }

    private static string ListProblem(string? items) =>
        RandomDraw.ParseList(items).Count == 0
            ? "Type the items, separated by commas."
            : $"Type at most {MaxListItems} items.";

    private string CallerName =>
        RandomDrawFormatter.Escape((Context.User as SocketGuildUser)?.DisplayName ?? Context.User.Username);

    private HashSet<ulong> Excluded(bool excludeMe, IUser? exclude) =>
        RandomPeople.Excluded(Context.User.Id, excludeMe, exclude?.Id);

    private (IVoiceChannel Channel, List<ulong> People)? ReadVoice(IVoiceChannel? picked, bool excludeMe, IUser? exclude)
    {
        var channel = picked ?? (Context.User as SocketGuildUser)?.VoiceChannel;
        if (channel is null || Context.Guild.GetVoiceChannel(channel.Id) is not { } socketChannel)
        {
            return null;
        }

        var people = socketChannel.ConnectedUsers.Select(user => new DrawPerson(
            user.Id, user.IsBot, user.IsSelfDeafened || user.IsDeafened, new HashSet<ulong>()));
        return (channel, RandomPeople.Eligible(people, Excluded(excludeMe, exclude)));
    }

    private List<ulong> ReadRolePeople(IReadOnlyCollection<IRole> roles, RoleMatch match, bool excludeMe, IUser? exclude)
    {
        // Members are cached (GuildMembers intent, AlwaysDownloadUsers), so this reads no API.
        var people = Context.Guild.Users.Select(user => new DrawPerson(
            user.Id, user.IsBot, IsDeafened: false, user.Roles.Select(r => r.Id).ToHashSet()));
        var withRoles = RandomPeople.WithRoles(people, roles.Select(r => r.Id).ToList(), match);
        return RandomPeople.Eligible(withRoles, Excluded(excludeMe, exclude));
    }

    /// <summary>The members to draw from, without the excluded Discord accounts; null for an unknown club.</summary>
    private async Task<(string PoolName, List<DrawableClubMember> Members, bool AllClubs)?> ReadClubMembersAsync(
        string? club, bool excludeMe, IUser? exclude)
    {
        var allClubs = string.Equals(club, DrawClubAutocompleteHandler.AllClubsValue, StringComparison.OrdinalIgnoreCase);
        Guid? clubId = null;
        if (!allClubs && !ClubOption.TryParse(club, out clubId))
        {
            return null;
        }

        var result = await Mediator.Send(new ReadDrawableClubMembersQuery(clubId, allClubs)).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return null;
        }

        var excluded = Excluded(excludeMe, exclude);
        var members = result.Value.Members
            .Where(member => member.DiscordUserId is not { } id || !excluded.Contains(id))
            .ToList();
        return (result.Value.PoolName, members, allClubs);
    }
}
