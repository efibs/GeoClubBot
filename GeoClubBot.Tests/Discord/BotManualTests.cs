using System.Text;
using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions;
using GeoClubBot.Discord.InputAdapters.Interactions.Help;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// <c>/help</c> is read from the commands themselves (#81), so these tests are what keeps it complete:
/// every command must belong to a feature and describe its parameters, and every page must fit Discord.
/// The snapshots of the real manual are where its wording is reviewed.
/// </summary>
public sealed class BotManualTests
{
    private static readonly IReadOnlyDictionary<string, ulong> NoIds = new Dictionary<string, ulong>();

    [Fact]
    public async Task Every_command_is_in_the_manual_and_described()
    {
        var manual = await RealManualAsync();

        manual.Problems.Should().BeEmpty();

        using var modules = await LoadedInteractionModules.LoadAsync();
        var listed = manual.Features.SelectMany(f => f.Commands).ToList();
        var slashCount = modules.Service.SlashCommands.Count(c => c.Module.Attributes.OfType<NotInManualAttribute>().Count() == 0);
        listed.Count(c => c.Kind == ManualCommandKind.Slash).Should().Be(slashCount);
        listed.Sum(c => c.Shortcuts.Count + (c.Kind == ManualCommandKind.Slash ? 0 : 1))
            .Should().Be(modules.Service.ContextCommands.Count);
    }

    [Fact]
    public async Task Every_page_fits_discord_for_an_admin_with_everything_on()
    {
        var features = (await RealManualAsync()).For(_ => true, _ => true);
        var ids = features.SelectMany(f => f.Commands).Select(c => c.Name.Split(' ')[0]).Distinct()
            .ToDictionary(name => name, _ => 1_234_567_890_123_456_789UL);

        var overview = HelpFormatter.Overview(features, ids);
        overview.Embed.Description.Length.Should().BeLessThanOrEqualTo(4096);

        foreach (var feature in features)
        {
            var pages = HelpFormatter.Pages(feature, ids);
            pages.Should().OnlyContain(page => page.Length <= HelpFormatter.MaxPageLength, $"{feature.Key} must fit an embed");

            for (var page = 0; page < pages.Count; page++)
            {
                var message = HelpFormatter.Feature(features, feature.Key, page, ids);
                message.Embed.Length.Should().BeLessThanOrEqualTo(6000);
                message.Components.Components.Should().HaveCountLessThanOrEqualTo(5);
            }
        }

        features.Count.Should().BeLessThanOrEqualTo(24, "the menu holds 25 options, one of them the overview");
    }

    [Fact]
    public async Task A_member_sees_no_admin_command_and_no_feature_that_is_off()
    {
        var manual = await RealManualAsync();

        var member = manual.For(toggle => toggle != HelpToggle.Ai, _ => false);

        member.Should().NotContain(f => f.Key == "ai");
        member.Should().NotContain(f => f.Key == "activity-checks", "every one of its commands is for admins");
        member.SelectMany(f => f.Commands).Should().OnlyContain(c => c.RequiredPermission == null);
        member.Should().Contain(f => f.Key == "reminders");
    }

    [Fact]
    public async Task The_ai_admin_commands_are_admin_only_but_the_quiz_is_open()
    {
        var ai = (await RealManualAsync()).Features.Single(f => f.Key == "ai");

        ai.Commands.Single(c => c.Name == "ai ingest").RequiredPermission.Should().Be(GuildPermission.Administrator);
        ai.Commands.Single(c => c.Name == "ai feedback-export").RequiredPermission.Should().Be(GuildPermission.Administrator);
        ai.Commands.Single(c => c.Name == "ai quiz").RequiredPermission.Should().BeNull();
    }

    [Fact]
    public Task The_manual_as_an_admin_with_everything_on_sees_it() => Verify(Render(_ => true));

    [Fact]
    public Task The_manual_as_a_member_with_everything_on_sees_it() => Verify(Render(_ => false));

    [Fact]
    public void A_long_page_is_split_between_commands_and_turned_with_buttons()
    {
        var commands = Enumerable.Range(1, 40)
            .Select(i => new ManualCommand(ManualCommandKind.Slash, $"long command-{i}", new string('x', 90),
                new string('y', 150), [new ManualParameter("value", "A value", Required: false, Default: "7")], null,
                HelpToggle.Always, []))
            .ToList();
        var feature = new ManualFeature("long", "📜", "Long", "A feature with many commands.", HelpToggle.Always, [], commands);

        var pages = HelpFormatter.Pages(feature, NoIds);

        pages.Should().HaveCountGreaterThan(1);
        pages.Should().OnlyContain(page => page.Length <= HelpFormatter.MaxPageLength);
        string.Join("\n\n", pages).Should().Contain("`/long command-40`").And.Contain("*(optional, default 7)*");

        var second = HelpFormatter.Feature([feature], "long", 1, NoIds);
        second.Embed.Footer!.Value.Text.Should().Be($"Page 2/{pages.Count}");
        var buttons = second.Components.Components.OfType<ActionRowComponent>().SelectMany(row => row.Components).OfType<ButtonComponent>().ToList();
        buttons.Select(b => b.CustomId).Should().Equal("help-page:long:0", "help-page:long:2");
    }

    [Fact]
    public void A_command_is_a_clickable_mention_when_its_id_is_known()
    {
        var ids = new Dictionary<string, ulong> { ["daily-reminder"] = 42 };

        HelpFormatter.Mention("daily-reminder add", ids).Should().Be("</daily-reminder add:42>");
        HelpFormatter.Mention("my-activity last-days", ids).Should().Be("`/my-activity last-days`");
    }

    [Fact]
    public void A_feature_the_member_cannot_see_falls_back_to_the_overview()
    {
        var message = HelpFormatter.Feature([], "ai", 0, NoIds);

        message.Embed.Title.Should().Be("🌍 GeoClubBot manual");
    }

    [Fact]
    public void A_failed_command_points_to_its_feature_page_even_from_a_nested_module()
    {
        HelpHint.For(typeof(ActivityModule.ActivityStrikeModule))
            .Should().Be("-# `/help` → 🛡️ Activity checks explains these commands.");
    }

    private static async Task<BotManual> RealManualAsync()
    {
        using var modules = await LoadedInteractionModules.LoadAsync();
        return BotManual.Build(modules.Service.SlashCommands, modules.Service.ContextCommands);
    }

    /// <summary>Every page of the real manual, as one member sees it with every feature on and no command ids.</summary>
    private static async Task<string> Render(Func<GuildPermission, bool> has)
    {
        var features = (await RealManualAsync()).For(_ => true, has);
        var text = new StringBuilder();

        var overview = HelpFormatter.Overview(features, NoIds);
        text.AppendLine($"# {overview.Embed.Title}").AppendLine(overview.Embed.Description).AppendLine($"-- {overview.Embed.Footer?.Text}");

        foreach (var feature in features)
        {
            var pages = HelpFormatter.Pages(feature, NoIds);
            for (var page = 0; page < pages.Count; page++)
            {
                var message = HelpFormatter.Feature(features, feature.Key, page, NoIds);
                text.AppendLine().AppendLine($"# {message.Embed.Title}").AppendLine(message.Embed.Description);
                if (message.Embed.Footer is { } footer)
                {
                    text.AppendLine($"-- {footer.Text}");
                }
            }
        }

        return text.ToString();
    }
}
