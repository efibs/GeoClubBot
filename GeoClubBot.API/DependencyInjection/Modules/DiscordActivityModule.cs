using Infrastructure.OutputAdapters.Discord;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.DependencyInjection.Modules;

/// <summary>
/// Registers the HTTP integration backing the Club Dashboard Discord Activity — the typed
/// <see cref="IDiscordOAuthService"/> client used for the OAuth2 token exchange and user lookup.
/// </summary>
public static class DiscordActivityModule
{
    public static IServiceCollection AddDiscordActivityModule(this IServiceCollection services)
    {
        services.AddHttpClient<IDiscordOAuthService, DiscordOAuthService>(client =>
        {
            client.BaseAddress = new Uri("https://discord.com/api/");
        });

        return services;
    }
}
