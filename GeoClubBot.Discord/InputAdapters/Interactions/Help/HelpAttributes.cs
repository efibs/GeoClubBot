namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>
/// Puts a module's commands (and those of its nested modules) on one page of <c>/help</c>.
/// Exactly one module per <see cref="Key"/> describes the feature (<see cref="Title"/> and the rest);
/// every other module of that feature names only the key.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HelpFeatureAttribute(string key) : Attribute
{
    /// <summary>What <c>/help feature:</c> takes and the page's component ids carry: lowercase, no colon.</summary>
    public string Key { get; } = key;

    public string? Emoji { get; init; }

    public string? Title { get; init; }

    /// <summary>One sentence for the overview and the feature menu; at most 100 characters (a menu option's limit).</summary>
    public string? Summary { get; init; }

    /// <summary>Where the feature stands in the overview and the menu, smallest first.</summary>
    public int Order { get; init; }

    /// <summary>The setting that switches the whole feature on; while it is off the feature is left out.</summary>
    public HelpToggle ShownWhen { get; init; } = HelpToggle.Always;

    /// <summary>Whether this declaration describes the feature, rather than only naming it.</summary>
    public bool Describes => Title is not null;
}

/// <summary>
/// A paragraph of a feature's page that belongs to no single command, such as how to ask the AI. Shown
/// above the commands, by <see cref="Order"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HelpSectionAttribute(int order, string title, string text) : Attribute
{
    public int Order { get; } = order;

    public string Title { get; } = title;

    /// <summary>Discord markdown.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// What a command's 100-character description has no room for: formats, what happens next, examples.
/// A right-click command has no description of its own, so for one without a slash twin this is its
/// description.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HelpDetailAttribute(string text) : Attribute
{
    /// <summary>Discord markdown.</summary>
    public string Text { get; } = text;
}

/// <summary>Leaves a command out of the manual while a setting is off, e.g. <c>/ai quiz</c> without the quiz.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HelpShownWhenAttribute(HelpToggle toggle) : Attribute
{
    public HelpToggle Toggle { get; } = toggle;
}

/// <summary>
/// Marks a right-click command as the shortcut of a slash command declared in another method of the same
/// module (one whose method has both attributes is paired by itself), so the manual shows the two together.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HelpShortcutForAttribute(string slashCommandMethodName) : Attribute
{
    public string SlashCommandMethodName { get; } = slashCommandMethodName;
}

/// <summary>Keeps a module's commands out of the manual: only <c>/help</c> itself.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NotInManualAttribute : Attribute;

/// <summary>The settings a feature or command of the manual depends on; read by <see cref="BotManualService"/>.</summary>
public enum HelpToggle
{
    Always,
    Ai,
    AiQuiz,
    AiRoundReview,
    AiFeedback,
    CountryChallenges,
    ClubChatForwarding,
    SelfRoles,
    MissionBoardNotifications
}
