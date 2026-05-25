using Configuration;
using GeoClubBot.DependencyInjection.Modules;
using Infrastructure.OutputAdapters.GeoGuessr;
using Refit;
using UseCases.OutputPorts.GeoGuessr;

namespace GeoClubBot.DependencyInjection;

/// <summary>
/// Composition root entry point. Delegates each bounded slice to its own module.
/// </summary>
public static class ClubBotServices
{
    public static void AddClubBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistenceModule(configuration);
        services.AddDiscordAdaptersModule();
        services.AddGeoGuessrIntegrationModule();
        services.AddRenderingModule();
        services.AddStrikesModule();
        services.AddExcusesModule();
        services.AddClubMembersModule();
        services.AddAccountLinkingModule();
        services.AddDailyChallengesModule();
        services.AddSelfRolesModule();
        services.AddQuartzModule();
        services.AddAiServicesIfConfigured(configuration);
    }

    public static void AddGeoGuessrHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        var geoGuessrConfig = new GeoGuessrConfiguration
        {
            SyncSchedule = null!,
            ActivityNcfaToken = null!,
            MissionsNcfaToken = null!,
            UserProfileNcfaToken = null!,
            Clubs = null!
        };
        configuration.GetSection(GeoGuessrConfiguration.SectionName).Bind(geoGuessrConfig);

        foreach (var club in geoGuessrConfig.Clubs)
        {
            services.AddHttpClient($"GeoGuessr_{club.ClubId}", client =>
                {
                    client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                    client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={club.NcfaToken}");
                })
                .AddResilienceHandler($"GeoGuessrApiResiliencePipeline_{club.ClubId}",
                    ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);
        }

        services.AddHttpClient(GeoGuessrClientFactory.ActivityHttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrConfig.ActivityNcfaToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline_Activity",
                ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);

        services.AddHttpClient(GeoGuessrClientFactory.MissionsHttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrConfig.MissionsNcfaToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline_Missions",
                ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);

        services.AddHttpClient(GeoGuessrClientFactory.UserProfileHttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrConfig.UserProfileNcfaToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline_UserProfile",
                ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);

        services.AddSingleton<IGeoGuessrClientFactory, GeoGuessrClientFactory>();

        services.AddRefitClient<IGeoGuessrApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://www.geoguessr.com/api");
                client.DefaultRequestHeaders.Add("Cookie", $"_ncfa={geoGuessrConfig.MainClub.NcfaToken}");
            })
            .AddResilienceHandler("GeoGuessrApiResiliencePipeline",
                ResiliencePipelines.AddGeoGuessrApiResiliencePipeline);

        services.AddTransient<IGeoGuessrClient, RefitGeoGuessrClient>();
    }
}
