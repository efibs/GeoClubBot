using System.Net.Http.Headers;
using Configuration;
using Constants;
using GeoClubBot.Services;
using Infrastructure.OutputAdapters.AI;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qdrant.Client;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI;

namespace GeoClubBot.DependencyInjection;

public static class AiServices
{
    public static void AddAiServicesIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        var aiConfig = configuration.GetSection(AiConfiguration.SectionName).Get<AiConfiguration>() ?? new AiConfiguration();

        // Registered even when the feature is off. MediatR's assembly scan picks up every handler in
        // the Application assembly unconditionally, so the container must be able to construct the
        // AI handlers' dependencies or service-descriptor validation fails at start-up. Nothing here
        // performs I/O until it is called, and the hosted services and jobs that would call it are
        // gated below and on AiConfiguration.Active respectively.
        services.AddOpenRouterServices(aiConfig);

        if (!aiConfig.Active)
        {
            return;
        }

        var qdrantConnectionString = configuration.GetConnectionString(ConfigKeys.QDrantConnectionString)!;
        var embeddingEndpoint = configuration.GetConnectionString(ConfigKeys.EmbeddingEndpoint)!;
        var embeddingModelName = aiConfig.EmbeddingModel!;

        services.AddHostedService<AiBotService>();

        services.AddTransient(_ => new QdrantClient(qdrantConnectionString));

        services.AddTransient<VllmEmbeddingService>(_ =>
            new VllmEmbeddingService(new Uri(embeddingEndpoint), embeddingModelName));

        // Split components: page-fetching (Puppeteer), embedding (vLLM + categoriser), and
        // the vector index (Qdrant). The PlonkItGuideVectorStore facade composes them.
        services.AddSingleton<IPlonkItPageFetcher, PuppeteerPlonkItPageFetcher>();
        services.AddSingleton<IPlonkItVectorIndex, QdrantPlonkItVectorIndex>();
        services.AddSingleton<IPlonkItEmbedder, VllmPlonkItEmbedder>();

        services.AddSingleton<PlonkItGuideVectorStore>();
        services.AddSingleton<IPlonkItGuideVectorStore>(sp => sp.GetRequiredService<PlonkItGuideVectorStore>());

        services.AddTransient<PlonkItGuidePlugin>();

        services.AddTransient<IPlonkItGuideEmbeddingTextProvider, PlonkItGuideEmbeddingTextProvider>();

        // MediatR's assembly scan only sees the Application assembly; the AI chat handler
        // lives in Infrastructure (it needs SemanticKernel), so register it manually.
        services.AddTransient<IRequestHandler<GetAiResponseQuery, string?>, GeoGuessrChatBotHandler>();
    }

    /// <summary>
    /// Chat generation via OpenRouter, with the model chosen automatically from whatever is free
    /// today rather than pinned in configuration.
    /// </summary>
    private static void AddOpenRouterServices(this IServiceCollection services, AiConfiguration aiConfig)
    {
        var openRouter = aiConfig.OpenRouter;

        services.AddHttpClient(RefitChatModelClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(openRouter.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(aiConfig.RequestTimeoutSeconds);

                if (!string.IsNullOrWhiteSpace(openRouter.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", openRouter.ApiKey);
                }

                // Optional attribution headers; OpenRouter surfaces them on their public leaderboards.
                if (!string.IsNullOrWhiteSpace(openRouter.SiteUrl))
                {
                    client.DefaultRequestHeaders.Add("HTTP-Referer", openRouter.SiteUrl);
                }

                client.DefaultRequestHeaders.Add("X-Title", openRouter.AppName);
            })
            .AddResilienceHandler(
                "OpenRouterResiliencePipeline",
                builder => ResiliencePipelines.AddOpenRouterResiliencePipeline(builder, openRouter.PerMinuteRequestBudget));

        // TimeProvider is not otherwise used in this solution; registering the system implementation
        // keeps the catalog's failure-decay logic swappable in tests without a new dependency.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IChatModelClient, RefitChatModelClient>();

        // Singleton: the roster is process-wide state, and the failure tracker only demotes a flaky
        // model usefully once penalties accumulate across turns.
        services.AddSingleton<IChatModelCatalog, ChatModelCatalog>();
    }
}
