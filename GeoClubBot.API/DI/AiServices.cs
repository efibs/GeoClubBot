using Constants;
using GeoClubBot.Services;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using UseCases.InputPorts.AI;
using UseCases.UseCases.AI;

namespace GeoClubBot.DI;

public static class AiServices
{
    public static void AddAiServicesIfConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        // Get if the ai is active
        var aiActive = configuration.GetValue(ConfigKeys.AiActiveConfigurationKey, false);

        // If the ai is not active
        if (aiActive == false)
        {
            // Do nothing
            return;
        }
        
        // Get the configured values
        var qdrantConnectionString = configuration.GetConnectionString(ConfigKeys.QDrantConnectionString)!;
        var embeddingEndpoint = configuration.GetConnectionString(ConfigKeys.EmbeddingEndpoint)!;
        var embeddingModelName = configuration.GetValue<string>(ConfigKeys.EmbeddingModelNameConfigurationKey)!;
        
        // Add the chatbot service
        services.AddHostedService<AiBotService>();
        
        // Add the qdrant client
        services.AddTransient(_ => new QdrantClient(qdrantConnectionString));
        
        // Add the embedding service
        services.AddTransient<ITextEmbeddingGenerationService>(_ =>
            new VllmEmbeddingService(new Uri(embeddingEndpoint), embeddingModelName));
        
        // Add the meta vector store
        services.AddTransient<MetaVectorStore>();
        
        // Add the meta search plugin
        services.AddTransient<IMetaVectorStoreSearchPlugin, MetaVectorStoreSearchPlugin>();
        
        // Add the chat bot use case
        services.AddTransient<IGeoGuessrChatBotUseCase, GeoGuessrChatBotUseCase>();
    }
}