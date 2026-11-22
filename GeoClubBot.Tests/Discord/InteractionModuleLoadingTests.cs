using System.Text.RegularExpressions;
using Constants;
using Discord;
using Discord.Interactions;
using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Every interaction module must load, and every slash command must fit Discord's limits.
///
/// Modules are only loaded when the bot connects, which no other test does — the end-to-end host strips
/// the gateway. A module that does not load, or a description one character over the limit, therefore
/// only shows when the deployed bot starts and every command registration fails with it.
/// </summary>
public sealed partial class InteractionModuleLoadingTests
{
    private const int MaxDescriptionLength = 100;

    [Fact]
    public async Task Every_module_loads_and_every_command_fits_discords_limits()
    {
        // Discord.Net builds every module once while loading it, so each constructor dependency must resolve.
        using var modules = await LoadedInteractionModules.LoadAsync();
        var service = modules.Service;

        service.SlashCommands.Should().NotBeEmpty();

        var problems = new List<string>();

        foreach (var group in service.Modules.Where(m => m.IsSlashGroup))
        {
            Check(problems, $"group /{group.SlashGroupName}", group.SlashGroupName, group.Description);
        }

        foreach (var command in service.SlashCommands)
        {
            var path = $"/{string.Join(' ', GroupNames(command.Module).Append(command.Name))}";
            Check(problems, path, command.Name, command.Description);

            foreach (var parameter in command.Parameters)
            {
                Check(problems, $"{path} {parameter.Name}", parameter.Name, parameter.Description);
            }
        }

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// The quiz buttons' ids must reach their handlers. A wildcard in Discord.Net needs at least one
    /// character, so an id with an empty part silently matched nothing (#479).
    /// </summary>
    [Theory]
    [InlineData(null, false, null, false)]
    [InlineData(null, true, AiQuizDifficulty.Easy, false)]
    [InlineData("Vietnam", false, AiQuizDifficulty.Hard, true)]
    [InlineData("South Africa", true, null, true)]
    public async Task Every_quiz_button_reaches_its_handler(string? country, bool lesserKnownOnly, AiQuizDifficulty? difficulty, bool revealed)
    {
        using var modules = await LoadedInteractionModules.LoadAsync();
        var service = modules.Service;

        var round = QuizRounds.Round(askedCountry: country, askedDifficulty: difficulty, lesserKnownOnly: lesserKnownOnly);
        if (revealed)
        {
            round.Reveal(QuizRounds.AskedAt);
        }

        var buttonIds = AiQuizFormatter.Buttons(round).Components
            .OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .Select(button => button.CustomId)
            .Concat([ComponentIds.AiQuizRightButtonId, ComponentIds.AiQuizWrongButtonId, "ai-quiz-again:0:"])
            .ToList();

        buttonIds.Should().HaveCount(revealed ? 8 : 9);
        foreach (var id in buttonIds)
        {
            var interaction = Substitute.For<IComponentInteraction>();
            interaction.Data.CustomId.Returns(id);

            service.SearchComponentCommand(interaction).IsSuccess.Should().BeTrue($"the button id '{id}' must reach a handler");
        }
    }

    /// <summary>
    /// Discord takes a command's default permissions from its top-level group only, so a subcommand's own
    /// <c>[DefaultMemberPermissions]</c> restricts nothing: every member could run the /ai admin commands
    /// until #81. A subcommand that needs more than its group must check at run time.
    /// </summary>
    [Fact]
    public async Task No_subcommand_relies_on_a_permission_discord_ignores()
    {
        using var modules = await LoadedInteractionModules.LoadAsync();

        var ignored = modules.Service.SlashCommands
            .Where(command => !command.IsTopLevelCommand && command.DefaultMemberPermissions is { } own && own != 0)
            .Where(command =>
            {
                var group = TopLevelGroup(command.Module);
                var registered = group.DefaultMemberPermissions ?? 0;
                var checkedAtRun = command.Preconditions.OfType<RequireUserPermissionAttribute>()
                    .Select(p => p.GuildPermission ?? 0)
                    .Aggregate((GuildPermission)0, (all, p) => all | p);
                return (command.DefaultMemberPermissions!.Value & ~(registered | checkedAtRun)) != 0;
            })
            .Select(command => $"/{string.Join(' ', GroupNames(command.Module).Append(command.Name))}")
            .ToList();

        ignored.Should().BeEmpty("Discord ignores these subcommands' [DefaultMemberPermissions]; use [RequireUserPermission]");
    }

    [Theory]
    [InlineData(ComponentIds.HelpFeatureMenuId)]
    [InlineData("help-page:ai:1")]
    [InlineData("help-page:activity-checks:0")]
    public async Task Every_help_component_reaches_its_handler(string id)
    {
        using var modules = await LoadedInteractionModules.LoadAsync();

        var interaction = Substitute.For<IComponentInteraction>();
        interaction.Data.CustomId.Returns(id);

        modules.Service.SearchComponentCommand(interaction).IsSuccess.Should().BeTrue($"the id '{id}' must reach a handler");
    }

    private static ModuleInfo TopLevelGroup(ModuleInfo module)
    {
        ModuleInfo? group = null;
        for (ModuleInfo? current = module; current is not null; current = current.Parent)
        {
            if (current.IsSlashGroup)
            {
                group = current;
            }
        }

        return group ?? module;
    }

    private static IEnumerable<string> GroupNames(ModuleInfo? module)
    {
        var names = new List<string>();
        for (; module is not null; module = module.Parent)
        {
            if (module.IsSlashGroup)
            {
                names.Insert(0, module.SlashGroupName);
            }
        }

        return names;
    }

    private static void Check(List<string> problems, string path, string name, string? description)
    {
        if (!NameRegex().IsMatch(name))
        {
            problems.Add($"{path}: name '{name}' must be 1-32 lowercase letters, digits, '-' or '_'.");
        }

        if (string.IsNullOrEmpty(description) || description.Length > MaxDescriptionLength)
        {
            problems.Add($"{path}: description must be 1-{MaxDescriptionLength} characters, but is {description?.Length ?? 0}.");
        }
    }

    [GeneratedRegex(@"^[-_\p{Ll}\p{Lo}\p{N}]{1,32}$")]
    private static partial Regex NameRegex();
}
