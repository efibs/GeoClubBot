using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Discord.InputAdapters.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Every interaction module of the bot, loaded into an <see cref="InteractionService"/> the way the bot loads
/// them when it connects, without a connection.
/// </summary>
internal sealed class LoadedInteractionModules : IDisposable
{
    private readonly DiscordSocketClient _client;

    private LoadedInteractionModules(DiscordSocketClient client, InteractionService service)
    {
        _client = client;
        Service = service;
    }

    public InteractionService Service { get; }

    public static async Task<LoadedInteractionModules> LoadAsync()
    {
        var client = new DiscordSocketClient();
        var service = new InteractionService(client);
        await service.AddModulesAsync(typeof(INteractionsAssemblyMarker).Assembly, new SubstituteServiceProvider());
        return new LoadedInteractionModules(client, service);
    }

    public void Dispose()
    {
        Service.Dispose();
        _client.Dispose();
    }

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
