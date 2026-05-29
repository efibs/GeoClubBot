using Constants;
using GeoClubBot.Services;
using Infrastructure.OutputAdapters.AI;
using MediatR;
using Qdrant.Client;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI;

namespace GeoClubBot.DependencyInjection;

public static class AiServices
{
    public static void AddAiServicesIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        var aiActive = configuration.GetValue(ConfigKeys.AiActiveConfigurationKey, false);
        if (!aiActive)
        {
            return;
        }

        var qdrantConnectionString = configuration.GetConnectionString(ConfigKeys.QDrantConnectionString)!;
        var embeddingEndpoint = configuration.GetConnectionString(ConfigKeys.EmbeddingEndpoint)!;
        var embeddingModelName = configuration.GetValue<string>(ConfigKeys.EmbeddingModelNameConfigurationKey)!;

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
}
