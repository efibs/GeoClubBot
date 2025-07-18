using Constants;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GeoClubBot.Services;
using Infrastructure;
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

        // Add auxiliary services
        services.AddSingleton<DiscordBotReadyService>();
        
        // Add the input adapters
        services.AddHostedService<ActivityCheckService>();
        services.AddHostedService<CheckClubLevelService>();

        // Add the output adapters 
        services.AddTransient<IGeoGuessrAccess, HttpGeoGuessrAccess>();
        services.AddTransient<IActivityRepository, FileActivityRepository>();
        services.AddTransient<IActivityStatusMessageSender, DiscordActivityStatusMessageSender>();
        services.AddTransient<IExcusesRepository, FileExcusesRepository>();
        services.AddTransient<IStatusUpdater, DiscordStatusUpdater>();
        services.AddTransient<IMessageSender, DiscordMessageSender>();

        // Add the use cases
        services.AddTransient<ICheckGeoGuessrPlayerActivityUseCase, CheckGeoGuessrPlayerActivityUseCase>();
        services.AddTransient<IReadMemberNumStrikesUseCase, ReadMemberNumStrikesUseCase>();
        services.AddTransient<IWriteMemberNumStrikesUseCase, WriteMemberNumStrikesUseCase>();
        services.AddTransient<IAddExcuseUseCase, AddExcuseUseCase>();
        services.AddTransient<IRemoveExcuseUseCase, RemoveExcuseUseCase>();
        services.AddTransient<IIsPlayerTrackedUseCase, IsPlayerTrackedUseCase>();
        services.AddTransient<IReadExcusesUseCase, ReadExcusesUseCase>();
        services.AddTransient<ICleanupUseCase, CleanupUseCase>();
        services.AddTransient<IGetLastCheckTimeUseCase, GetLastCheckTimeUseCase>();
        services.AddSingleton<ICheckClubLevelUseCase, CheckClubLevelUseCase>();
    }
}