using System.Text.RegularExpressions;
using Discord.Interactions;
using Discord.WebSocket;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        using var client = new DiscordSocketClient();
        using var service = new InteractionService(client);

        // Discord.Net builds every module once while loading it, so each constructor dependency must resolve.
        await service.AddModulesAsync(typeof(INteractionsAssemblyMarker).Assembly, new SubstituteServiceProvider());

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

    /// <summary>
    /// A fake for every interface a module asks for; nothing here ever handles an interaction. Discord.Net
    /// resolves modules from a scope, so the provider is its own scope factory and scope.
    /// </summary>
    private sealed class SubstituteServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            // Some modules read their options in the constructor, so these must hold a real (default) value.
            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IOptions<>))
            {
                var value = Activator.CreateInstance(serviceType.GenericTypeArguments[0]);
                return typeof(Options).GetMethod(nameof(Options.Create))!
                    .MakeGenericMethod(serviceType.GenericTypeArguments[0])
                    .Invoke(null, [value]);
            }

            return serviceType.IsInterface ? Substitute.For([serviceType], []) : null;
        }

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }
}
