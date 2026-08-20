using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        services.AddKnowledgeIndex(configuration, aiConfig);

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

        // Embeddings share the chat client's HTTP pipeline, so they inherit its auth and rate limiter.
        services.AddSingleton<IEmbedder, OpenRouterEmbedder>();
    }

    /// <summary>
    /// The vector store holding indexed guide content. Registered unconditionally for the same reason
    /// as the chat services: Application handlers that depend on it are discovered by MediatR's
    /// assembly scan whether or not the feature is enabled. Nothing connects to Qdrant until a query
    /// or an ingest actually runs.
    /// </summary>
    private static void AddKnowledgeIndex(
        this IServiceCollection services,
        IConfiguration configuration,
        AiConfiguration aiConfig)
    {
        // Defaulted rather than null-forgiving: with AI off the connection string is legitimately
        // absent, and resolving the client must not throw just because the graph was built.
        var qdrantConnectionString =
            configuration.GetConnectionString(ConfigKeys.QDrantConnectionString) ?? "localhost";

        services.AddSingleton<IKnowledgeIndex>(_ => new QdrantKnowledgeIndex(
            new QdrantClient(qdrantConnectionString),
            BuildCollectionName(aiConfig),
            aiConfig.OpenRouter.EmbeddingDimensions));
    }

    /// <summary>
    /// Derives the collection name from the embedding model and its width, so changing either lands
    /// on a new collection instead of appending incomparable vectors to the existing one. The old
    /// collection is left in place, visible and deletable, rather than silently corrupted.
    /// </summary>
    private static string BuildCollectionName(AiConfiguration aiConfig)
    {
        var openRouter = aiConfig.OpenRouter;
        var modelFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(openRouter.EmbeddingModelId)))[..8]
            .ToLowerInvariant();

        return $"{aiConfig.KnowledgeCollectionPrefix}-{modelFingerprint}-{openRouter.EmbeddingDimensions}";
    }
}
