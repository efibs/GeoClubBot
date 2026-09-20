using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Configuration;
using Constants;
using GeoClubBot.Services;
using Infrastructure.OutputAdapters.AI;
using Infrastructure.OutputAdapters.AI.Extractors;
using Infrastructure.OutputAdapters.AI.ImageRelay;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qdrant.Client;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.AI.Ingestion;

namespace GeoClubBot.DependencyInjection;

public static class AiServices
{
    public static void AddAiServicesIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        var aiConfig = configuration.GetSection(AiConfiguration.SectionName).Get<AiConfiguration>() ?? new AiConfiguration();

        var ingestionConfig = configuration.GetSection(AiIngestionConfiguration.SectionName)
            .Get<AiIngestionConfiguration>() ?? new AiIngestionConfiguration();

        // Registered even when the feature is off. MediatR's assembly scan picks up every handler in
        // the Application assembly unconditionally, so the container must be able to construct the
        // AI handlers' dependencies or service-descriptor validation fails at start-up. Nothing here
        // performs I/O until it is called, and the listener below is only added when AI is enabled.
        services.AddOpenRouterServices(aiConfig);
        services.AddKnowledgeIndex(configuration, aiConfig);
        services.AddSourceExtractors(ingestionConfig);

        if (!aiConfig.Active)
        {
            return;
        }

        services.AddHostedService<AiConversationGateway>();
    }

    /// <summary>
    /// Chat generation and embeddings via OpenRouter, with the chat model chosen automatically from
    /// whatever is free today rather than pinned in configuration.
    /// </summary>
    private static void AddOpenRouterServices(this IServiceCollection services, AiConfiguration aiConfig)
    {
        var openRouter = aiConfig.OpenRouter;

        services.AddHttpClient(RefitChatModelClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(openRouter.BaseUrl);

                // The whole call: queueing for a token, waiting out a rate-limit window, and the retry
                // after it. Each attempt is bounded separately, inside the pipeline.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(aiConfig.OverallTimeoutSeconds, aiConfig.RequestTimeoutSeconds));

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
            .AddHttpMessageHandler(() => new ResilienceRejectionHandler())
            .AddResilienceHandler(
                "OpenRouterResiliencePipeline",
                builder => ResiliencePipelines.AddOpenRouterResiliencePipeline(
                    builder,
                    openRouter.PerMinuteRequestBudget,
                    TimeSpan.FromSeconds(aiConfig.RequestTimeoutSeconds)));

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
    /// Extractors that read third-party guide content, plus the registry that picks between them.
    /// Adding a source family is one extractor class and one line here.
    /// </summary>
    private static void AddSourceExtractors(this IServiceCollection services, AiIngestionConfiguration ingestionConfig)
    {
        // The whole fetch: queueing behind the politeness limiter, the attempts, and the backoff
        // between them. Each attempt is bounded separately, inside the pipeline — one client timeout
        // doing both jobs is what abandoned a slow Google Docs export before its first try was over.
        var overallTimeout = TimeSpan.FromSeconds(
            Math.Max(ingestionConfig.SourceOverallTimeoutSeconds, ingestionConfig.SourceRequestTimeoutSeconds));

        var attemptTimeout = TimeSpan.FromSeconds(ingestionConfig.SourceRequestTimeoutSeconds);

        services.AddHttpClient(PlonkItSourceExtractor.HttpClientName, client =>
            {
                // Identify ourselves: an unattended reader that says who it is and how to reach the
                // author is one a site operator can contact rather than simply block.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");
                client.Timeout = overallTimeout;
            })
            // Outside the pipeline, so a guide host that trips the circuit breaker fails one source
            // rather than escaping the extractor and ending the whole run.
            .AddHttpMessageHandler(() => new ResilienceRejectionHandler())
            .AddResilienceHandler(
                "ContentSourceResiliencePipeline",
                builder => ResiliencePipelines.AddContentSourceResiliencePipeline(builder, attemptTimeout));

        // Registered against both ports: the same adapter knows how to list the site's pages and how
        // to read one of them.
        services.AddSingleton<PlonkItSourceExtractor>();
        services.AddSingleton<ISourceExtractor>(sp => sp.GetRequiredService<PlonkItSourceExtractor>());
        services.AddSingleton<ISourceCatalog>(sp => sp.GetRequiredService<PlonkItSourceExtractor>());

        // The second guide site that indexes itself: fewer countries, far more detail per country.
        services.AddSingleton<RmrgSourceExtractor>();
        services.AddSingleton<ISourceExtractor>(sp => sp.GetRequiredService<RmrgSourceExtractor>());
        services.AddSingleton<ISourceCatalog>(sp => sp.GetRequiredService<RmrgSourceExtractor>());

        // One extractor per source family. The registry picks between them, so a new family is a
        // class plus a line here.
        services.AddSingleton<ISourceExtractor, ImgurAlbumSourceExtractor>();
        services.AddSingleton<ISourceExtractor, GoogleDocSourceExtractor>();
        services.AddSingleton<ISourceExtractor, GoogleSlidesSourceExtractor>();
        services.AddSingleton<ISourceExtractor, GoogleSheetSourceExtractor>();
        services.AddSingleton<ISourceExtractor, DirectImageSourceExtractor>();

        // A community library published as a spreadsheet; opt-in, and inert until a sheet id is set.
        services.AddSingleton<ISourceCatalog, MetaLibrarySourceCatalog>();

        services.AddSingleton<ISourceExtractorRegistry, SourceExtractorRegistry>();

        // Fetches images from hosts that refuse the AI provider, on the same polite pipeline as the
        // rest of the ingestion traffic.
        services.AddHttpClient(FileSystemImageRelay.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "GeoClubBot/1.0 (+https://github.com/efibs/geo-club-bot)");
                client.Timeout = overallTimeout;
            })
            // The relay chases redirects itself so it can rewrite the referer at each hop; letting the
            // handler follow them silently is what makes a regional image mirror answer 403.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler(() => new ResilienceRejectionHandler())
            .AddResilienceHandler(
                "ContentSourceResiliencePipeline",
                builder => ResiliencePipelines.AddContentSourceResiliencePipeline(builder, attemptTimeout));

        services.AddSingleton<IImageRelay, FileSystemImageRelay>();
    }

    /// <summary>
    /// The vector store holding indexed guide content. Registered unconditionally for the same reason
    /// as the chat services. Nothing connects to Qdrant until a query or an ingest actually runs.
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
