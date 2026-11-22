using System.Globalization;
using Discord;
using Discord.Interactions;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>
/// The manual <c>/help</c> shows, read from the loaded interaction modules: the commands' own names,
/// descriptions, parameters and permissions, plus the <c>Help*</c> attributes for what Discord has no
/// field for. Nothing about a command is written down twice, so the manual cannot drift from the bot.
/// </summary>
public sealed record BotManual(IReadOnlyList<ManualFeature> Features, IReadOnlyList<string> Problems)
{
    /// <summary>
    /// The features one member sees: only switched-on features and commands, and only commands the member
    /// may run. A feature left without a command is left out.
    /// </summary>
    public IReadOnlyList<ManualFeature> For(Func<HelpToggle, bool> isOn, Func<GuildPermission, bool> has) =>
        Features
            .Where(feature => isOn(feature.ShownWhen))
            .Select(feature => feature with
            {
                Commands = feature.Commands
                    .Where(command => isOn(command.ShownWhen))
                    .Where(command => command.RequiredPermission is not { } permission || has(permission))
                    .ToList()
            })
            .Where(feature => feature.Commands.Count > 0)
            .ToList();

    public static BotManual Build(IEnumerable<SlashCommandInfo> slashCommands, IEnumerable<ContextCommandInfo> contextCommands)
    {
        var problems = new List<string>();
        var slash = slashCommands.Where(command => !IsExcluded(command.Module)).ToList();
        var context = contextCommands.Where(command => !IsExcluded(command.Module)).ToList();

        var modules = slash.Select(command => command.Module)
            .Concat(context.Select(command => command.Module))
            .SelectMany(ModuleChain)
            .Distinct()
            .ToList();
        var declarations = modules.SelectMany(module => module.Attributes.OfType<HelpFeatureAttribute>()).ToList();

        var described = new Dictionary<string, HelpFeatureAttribute>(StringComparer.Ordinal);
        // Discord.Net hands a nested module its parent's attributes too, so the same declaration shows up
        // once per module; only a different description of the same key is a conflict.
        foreach (var declaration in declarations.Where(d => d.Describes).Distinct())
        {
            if (!described.TryAdd(declaration.Key, declaration))
            {
                problems.Add($"Feature '{declaration.Key}' is described by more than one module.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(declaration.Summary) || declaration.Summary.Length > MaxSummaryLength)
            {
                problems.Add($"Feature '{declaration.Key}' needs a summary of 1-{MaxSummaryLength} characters.");
            }

            if (string.IsNullOrWhiteSpace(declaration.Emoji))
            {
                problems.Add($"Feature '{declaration.Key}' needs an emoji.");
            }

            if (declaration.Key.Contains(':', StringComparison.Ordinal) || declaration.Key != declaration.Key.ToLowerInvariant())
            {
                problems.Add($"Feature key '{declaration.Key}' must be lowercase without ':'.");
            }
        }

        foreach (var key in declarations.Select(d => d.Key).Distinct().Where(key => !described.ContainsKey(key)))
        {
            problems.Add($"Feature '{key}' is named but no module describes it (Title, Emoji, Summary).");
        }

        var commands = new List<(string Key, ManualCommand Command)>();

        // Right-click commands paired with a slash command become a line under it instead of an entry.
        var shortcuts = new Dictionary<(ModuleInfo, string), List<ManualShortcut>>();
        var standalone = new List<ContextCommandInfo>();
        foreach (var command in context)
        {
            var target = command.Attributes.OfType<HelpShortcutForAttribute>().FirstOrDefault()?.SlashCommandMethodName
                ?? command.MethodName;

            if (slash.Any(s => s.Module == command.Module && s.MethodName == target))
            {
                var list = shortcuts.TryGetValue((command.Module, target), out var found) ? found : shortcuts[(command.Module, target)] = [];
                list.Add(new ManualShortcut(KindOf(command), command.Name));
            }
            else if (command.Attributes.OfType<HelpShortcutForAttribute>().Any())
            {
                problems.Add($"Right-click command '{command.Name}' is a shortcut for '{target}', which is no slash command of its module.");
            }
            else
            {
                standalone.Add(command);
            }
        }

        foreach (var command in slash)
        {
            var path = string.Join(' ', GroupNames(command.Module).Append(command.Name));
            if (FeatureKeyOf(command.Module) is not { } key)
            {
                problems.Add($"/{path} belongs to no feature: put [HelpFeature] on its module.");
                continue;
            }

            var parameters = command.Parameters.Select(parameter => Parameter(path, parameter, problems)).ToList();
            commands.Add((key, new ManualCommand(
                ManualCommandKind.Slash,
                path,
                command.Description,
                DetailOf(command),
                parameters,
                RequiredPermission(command, command.DefaultMemberPermissions),
                ToggleOf(command),
                shortcuts.TryGetValue((command.Module, command.MethodName), out var found) ? found : [])));
        }

        foreach (var command in standalone)
        {
            if (FeatureKeyOf(command.Module) is not { } key)
            {
                problems.Add($"Right-click command '{command.Name}' belongs to no feature: put [HelpFeature] on its module.");
                continue;
            }

            if (DetailOf(command) is not { } detail)
            {
                problems.Add($"Right-click command '{command.Name}' has no description: give it [HelpDetail].");
                continue;
            }

            commands.Add((key, new ManualCommand(
                KindOf(command), command.Name, detail, null, [],
                RequiredPermission(command, command.DefaultMemberPermissions), ToggleOf(command), [])));
        }

        var features = described.Values
            .OrderBy(feature => feature.Order)
            .ThenBy(feature => feature.Key, StringComparer.Ordinal)
            .Select(feature =>
            {
                var own = commands.Where(c => c.Key == feature.Key).Select(c => c.Command).ToList();
                var sections = modules
                    .Where(module => module.Attributes.OfType<HelpFeatureAttribute>().Any(d => d.Key == feature.Key))
                    .SelectMany(module => module.Attributes.OfType<HelpSectionAttribute>())
                    .Distinct()
                    .OrderBy(section => section.Order)
                    .Select(section => new ManualSection(section.Title, section.Text))
                    .ToList();

                return new ManualFeature(
                    feature.Key,
                    feature.Emoji ?? string.Empty,
                    feature.Title!,
                    feature.Summary ?? string.Empty,
                    feature.ShownWhen,
                    sections,
                    // Everyone's commands first, then what needs a permission; otherwise as declared.
                    own.OrderBy(c => c.RequiredPermission is null ? 0 : 1).ToList());
            })
            .ToList();

        return new BotManual(features, problems);
    }

    /// <summary>The feature a module's commands belong to, from it or the module it is nested in.</summary>
    public static string? FeatureKeyOf(ModuleInfo module) =>
        ModuleChain(module).SelectMany(m => m.Attributes.OfType<HelpFeatureAttribute>()).FirstOrDefault()?.Key;

    /// <summary>The <c>/a b c</c> group names leading to a command of the module, outermost first.</summary>
    public static IReadOnlyList<string> GroupNames(ModuleInfo module) =>
        ModuleChain(module).Where(m => m.IsSlashGroup).Select(m => m.SlashGroupName).Reverse().ToList();

    private const int MaxSummaryLength = 100;

    private static IEnumerable<ModuleInfo> ModuleChain(ModuleInfo? module)
    {
        for (; module is not null; module = module.Parent)
        {
            yield return module;
        }
    }

    private static bool IsExcluded(ModuleInfo module) =>
        ModuleChain(module).Any(m => m.Attributes.OfType<NotInManualAttribute>().Any());

    private static string? DetailOf(ICommandInfo command) =>
        command.Attributes.OfType<HelpDetailAttribute>().FirstOrDefault()?.Text;

    private static HelpToggle ToggleOf(ICommandInfo command) =>
        command.Attributes.OfType<HelpShownWhenAttribute>().FirstOrDefault()?.Toggle ?? HelpToggle.Always;

    private static ManualCommandKind KindOf(ContextCommandInfo command) =>
        command.CommandType == ApplicationCommandType.Message ? ManualCommandKind.Message : ManualCommandKind.User;

    /// <summary>
    /// The permission a member needs: Discord's default permission of the command or its top-level group
    /// (Discord ignores a subcommand's own), or a <see cref="RequireUserPermissionAttribute"/> on the way.
    /// </summary>
    private static GuildPermission? RequiredPermission(ICommandInfo command, GuildPermission? own)
    {
        var chain = ModuleChain(command.Module).ToList();
        var registered = command.IsTopLevelCommand || !chain.Any(m => m.IsSlashGroup)
            ? (own ?? 0) | (command.Module.DefaultMemberPermissions ?? 0)
            : chain.Last(m => m.IsSlashGroup).DefaultMemberPermissions ?? 0;

        var checkedAtRun = command.Preconditions
            .Concat(chain.SelectMany(m => m.Preconditions))
            .OfType<RequireUserPermissionAttribute>()
            .Select(p => p.GuildPermission ?? 0)
            .Aggregate((GuildPermission)0, (all, p) => all | p);

        var required = registered | checkedAtRun;
        return required == 0 ? null : required;
    }

    private static ManualParameter Parameter(string path, SlashCommandParameterInfo parameter, List<string> problems)
    {
        if (string.Equals(parameter.Description, parameter.Name, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"/{path} {parameter.Name}: give the parameter a [Summary(description: ...)].");
        }

        return new ManualParameter(parameter.Name, parameter.Description, parameter.IsRequired,
            parameter.IsRequired ? null : DefaultOf(parameter));
    }

    /// <summary>The default as a member would choose it: a choice's label, "on"/"off", or nothing for "left out".</summary>
    private static string? DefaultOf(SlashCommandParameterInfo parameter) => parameter.DefaultValue switch
    {
        null => null,
        bool value => value ? "on" : "off",
        var value when parameter.Choices.FirstOrDefault(c => Equals(c.Value?.ToString(), value.ToString())) is { } choice => choice.Name,
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        var value => value.ToString()
    };
}

public sealed record ManualFeature(
    string Key,
    string Emoji,
    string Title,
    string Summary,
    HelpToggle ShownWhen,
    IReadOnlyList<ManualSection> Sections,
    IReadOnlyList<ManualCommand> Commands);

public sealed record ManualSection(string Title, string Text);

/// <summary>
/// One entry of a feature page. <see cref="Name"/> is the full slash path without the slash
/// (<c>daily-reminder add</c>) or a right-click command's name.
/// </summary>
public sealed record ManualCommand(
    ManualCommandKind Kind,
    string Name,
    string Description,
    string? Detail,
    IReadOnlyList<ManualParameter> Parameters,
    GuildPermission? RequiredPermission,
    HelpToggle ShownWhen,
    IReadOnlyList<ManualShortcut> Shortcuts);

public enum ManualCommandKind
{
    Slash,
    User,
    Message
}

public sealed record ManualShortcut(ManualCommandKind Kind, string Name);

/// <summary><see cref="Default"/> is null for a required parameter, and for one whose default is "left out".</summary>
public sealed record ManualParameter(string Name, string Description, bool Required, string? Default);
