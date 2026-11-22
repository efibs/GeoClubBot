using System.Text;
using Constants;
using Discord;

namespace GeoClubBot.Discord.InputAdapters.Interactions.Help;

/// <summary>
/// Renders <c>/help</c>: the overview, or one feature's page, with the feature menu under it. A page too
/// long for one embed is split between commands, with buttons to turn the pages.
/// </summary>
public static class HelpFormatter
{
    /// <summary>The value the menu's overview option carries.</summary>
    public const string OverviewKey = "overview";

    /// <summary>Under Discord's 4096 for an embed description, so a title and footer still fit its 6000 in all.</summary>
    public const int MaxPageLength = 4000;

    private static readonly Color HelpColor = new(0x3B, 0x82, 0xF6);

    /// <param name="features">What the member may see, from <see cref="BotManual.For"/>.</param>
    /// <param name="commandIds">Top-level command name → its id in this server, for clickable mentions; a missing one is shown as code.</param>
    public static HelpMessage Overview(IReadOnlyList<ManualFeature> features, IReadOnlyDictionary<string, ulong> commandIds)
    {
        var text = new StringBuilder()
            .AppendLine("I help run the club: activity, reminders, stats, challenges and more. Pick a feature below to see its commands.")
            .AppendLine()
            .AppendLine("### Getting started")
            .AppendLine("- Type `/` in the message box: Discord lists every command and asks for each option as you go.")
            .AppendLine("- Right-click a member or a message (long-press on mobile) → **Apps** for shortcuts.")
            .AppendLine("- Most replies are only visible to you, so feel free to try things out.")
            .AppendLine()
            .AppendLine("### Features");

        foreach (var feature in features)
        {
            text.Append("**").Append(feature.Emoji).Append(' ').Append(feature.Title).Append("** — ").AppendLine(feature.Summary);

            var groups = feature.Commands
                .Where(c => c.Kind == ManualCommandKind.Slash)
                .Select(c => c.Name.Split(' ')[0])
                .Distinct()
                .Select(name => $"`/{name}`")
                .ToList();
            if (groups.Count > 0)
            {
                text.Append("-# ").AppendLine(string.Join(" · ", groups));
            }
        }

        var embed = new EmbedBuilder()
            .WithTitle("🌍 GeoClubBot manual")
            .WithColor(HelpColor)
            .WithDescription(Fit(text.ToString()))
            .WithFooter("/help or /manual · only what you can use is shown")
            .Build();

        return new HelpMessage(embed, Components(features, OverviewKey, page: 0, pageCount: 1));
    }

    /// <summary>
    /// One page of a feature, <paramref name="page"/> counted from 0 and clamped to the pages there are.
    /// An unknown <paramref name="key"/> (a feature since switched off, or not the member's) gives the overview.
    /// </summary>
    public static HelpMessage Feature(
        IReadOnlyList<ManualFeature> features, string key, int page, IReadOnlyDictionary<string, ulong> commandIds)
    {
        if (features.FirstOrDefault(f => f.Key == key) is not { } feature)
        {
            return Overview(features, commandIds);
        }

        var pages = Pages(feature, commandIds);
        page = Math.Clamp(page, 0, pages.Count - 1);

        var embed = new EmbedBuilder()
            .WithTitle($"{feature.Emoji} {feature.Title}")
            .WithColor(HelpColor)
            .WithDescription(pages[page]);

        if (pages.Count > 1)
        {
            embed.WithFooter($"Page {page + 1}/{pages.Count}");
        }

        return new HelpMessage(embed.Build(), Components(features, feature.Key, page, pages.Count));
    }

    /// <summary>The page texts of a feature: its summary and sections, then its commands, split between entries.</summary>
    public static IReadOnlyList<string> Pages(ManualFeature feature, IReadOnlyDictionary<string, ulong> commandIds)
    {
        var everyone = feature.Commands.Where(c => c.RequiredPermission is null).ToList();
        var restricted = feature.Commands.Where(c => c.RequiredPermission is not null).ToList();

        // Each block knows the heading it stands under, which is repeated when a page starts inside it.
        var blocks = new List<(string Text, string? Heading)>
        {
            (everyone.Count == 0 ? $"{feature.Summary}\n-# 🛡️ For admins only." : feature.Summary, null)
        };
        blocks.AddRange(feature.Sections.Select(section => ($"### {section.Title}\n{section.Text.Trim()}", (string?)null)));

        var commandsHeading = everyone.Count > 0 && (feature.Sections.Count > 0 || restricted.Count > 0) ? "### Commands" : null;
        blocks.AddRange(everyone.Select(c => (Entry(c, commandIds), commandsHeading)));
        blocks.AddRange(restricted.Select(c => (Entry(c, commandIds), everyone.Count > 0 ? "### 🛡️ Admins" : (string?)null)));

        var pages = new List<string>();
        var current = new StringBuilder();
        string? currentHeading = null;
        foreach (var (text, heading) in blocks)
        {
            // A heading goes with the entry after it, so a page never ends on one.
            var block = Fit(heading is not null && (heading != currentHeading || current.Length == 0) ? $"{heading}\n{text}" : text);

            if (current.Length > 0 && current.Length + 2 + block.Length > MaxPageLength)
            {
                pages.Add(current.ToString());
                current.Clear();
                if (heading is not null && heading == currentHeading)
                {
                    block = Fit($"{heading}\n{text}");
                }
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(block);
            currentHeading = heading;
        }

        pages.Add(current.ToString());
        return pages;
    }

    /// <summary>How a command is written in the manual: a clickable mention, or where to right-click.</summary>
    public static string Mention(string path, IReadOnlyDictionary<string, ulong> commandIds) =>
        commandIds.TryGetValue(path.Split(' ')[0], out var id) ? $"</{path}:{id}>" : $"`/{path}`";

    private static string Entry(ManualCommand command, IReadOnlyDictionary<string, ulong> commandIds)
    {
        var text = new StringBuilder();

        switch (command.Kind)
        {
            case ManualCommandKind.Slash:
                text.Append("**").Append(Mention(command.Name, commandIds)).Append("** — ").AppendLine(command.Description);
                break;
            default:
                text.Append(RightClick(command.Kind)).Append(" → **").Append(command.Name).Append("** — ").AppendLine(command.Description);
                break;
        }

        foreach (var parameter in command.Parameters)
        {
            text.Append("- `").Append(parameter.Name).Append('`');
            if (!parameter.Required)
            {
                text.Append(parameter.Default is { } value ? $" *(optional, default {value})*" : " *(optional)*");
            }

            text.Append(" — ").AppendLine(parameter.Description);
        }

        if (command.Detail is { } detail)
        {
            text.AppendLine(detail.Trim());
        }

        foreach (var shortcut in command.Shortcuts)
        {
            text.Append("-# Also: ").Append(RightClick(shortcut.Kind)).Append(" → **").Append(shortcut.Name).AppendLine("**");
        }

        return text.ToString().TrimEnd();
    }

    private static string RightClick(ManualCommandKind kind) =>
        kind == ManualCommandKind.Message ? "Right-click a message → Apps" : "Right-click a member → Apps";

    private static MessageComponent Components(IReadOnlyList<ManualFeature> features, string selected, int page, int pageCount)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId(ComponentIds.HelpFeatureMenuId)
            .WithPlaceholder("Pick a feature…")
            .AddOption("Overview", OverviewKey, "Everything I can do, and how commands work", new Emoji("🌍"), selected == OverviewKey);

        // Discord allows 25 options; the overview takes one.
        foreach (var feature in features.Take(24))
        {
            menu.AddOption(feature.Title, feature.Key, feature.Summary, new Emoji(feature.Emoji),
                feature.Key == selected);
        }

        var builder = new ComponentBuilder().WithSelectMenu(menu);

        if (pageCount > 1)
        {
            builder
                .WithButton("◀ Previous", $"{ComponentIds.HelpPageButtonId}:{selected}:{page - 1}", ButtonStyle.Secondary,
                    disabled: page == 0, row: 1)
                .WithButton("Next ▶", $"{ComponentIds.HelpPageButtonId}:{selected}:{page + 1}", ButtonStyle.Secondary,
                    disabled: page == pageCount - 1, row: 1);
        }

        return builder.Build();
    }

    /// <summary>A single block longer than a page (none is today) is cut rather than failing the reply.</summary>
    private static string Fit(string text) =>
        text.Length <= MaxPageLength ? text : string.Concat(text.AsSpan(0, MaxPageLength - 1), "…");
}

public sealed record HelpMessage(Embed Embed, MessageComponent Components);
