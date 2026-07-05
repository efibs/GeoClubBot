using GeoClubBot.Services;
using Infrastructure.OutputAdapters.Discord;
using UseCases.OutputPorts.Discord;

namespace GeoClubBot.DependencyInjection.Modules;

/// <summary>
/// Registers the services backing the Club Dashboard Discord Activity — the typed
/// <see cref="IDiscordOAuthService"/> client used for the OAuth2 token exchange and user lookup,
/// and the <see cref="ActivityViewerResolver"/> shared by the activity endpoints.
/// </summary>
public static class DiscordActivityModule
{
    public static IServiceCollection AddDiscordActivityModule(this IServiceCollection services)
    {
        services.AddHttpClient<IDiscordOAuthService, DiscordOAuthService>(client =>
        {
            client.BaseAddress = new Uri("https://discord.com/api/");
        });

        services.AddScoped<ActivityViewerResolver>();

        return services;
    }
}
