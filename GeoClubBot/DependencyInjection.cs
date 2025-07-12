using Constants;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using GeoClubBot.Services;
using Infrastructure.InputAdapters;
using Infrastructure.OutputAdapters;
using UseCases;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using RunMode = Discord.Interactions.RunMode;

namespace GeoClubBot;

/// <summary>
/// Helper class to register all required services in the dependency injection
/// </summary>
public static class DependencyInjection
{
    public static void AddClubBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Add the discord socket client
        services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged
        }));

        // Add the interaction service
        services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig
            {
                DefaultRunMode = RunMode.Async
            }));

        // Add the logging service and instantiate it immediately and 
        // therefore registering the logging callbacks.
        services.AddActivatedSingleton<DiscordLoggingService>();

        // Add the discord bot service
        services.AddSingleton<DiscordBotService>();
        services.AddHostedService(p => p.GetRequiredService<DiscordBotService>());

        // Add the command handler
        services.AddActivatedSingleton<InteractionHandler>();

        // Get the geoguessr token
        var geoGuessrToken = configuration.GetValue<string>(ConfigKeys.GeoGuessrTokenConfigurationKey);

        // Sanity check
        if (string.IsNullOrWhiteSpace(geoGuessrToken))
        {
            throw new InvalidOperationException("GeoGuessrToken is not set");
        }

        // Add the http client
        services.AddHttpClient(HttpClientConstants.GeoGuessrHttpClientName, client =>
        {
            // Set the base address
            client.BaseAddress = new Uri(HttpClientConstants.GeoGuessrBaseUrl);

            // Set the token in the cookies
            client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrToken}");
        });

        // Add the input ports
        services.AddHostedService<ActivityCheckService>();

        // Add the output ports 
        services.AddTransient<IGeoGuessrAccess, HttpGeoGuessrAccess>();
        services.AddTransient<IActivityRepository, FileActivityRepository>();
        services.AddTransient<IStatusMessageSender, DiscordStatusMessageSender>();

        // Add the use cases
        services.AddTransient<ICheckGeoGuessrPlayerActivityUseCase, CheckGeoGuessrPlayerActivityUseCase>();
        services.AddTransient<IReadMemberNumStrikesUseCase, ReadMemberNumStrikesUseCase>();
        services.AddTransient<IWriteMemberNumStrikesUseCase, WriteMemberNumStrikesUseCase>();
    }
}