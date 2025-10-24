using System.Net;
using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UseCases.InputPorts.AI;
using UseCases.OutputPorts;

namespace UseCases.UseCases.AI;

public class GeoGuessrChatBotUseCase : IGeoGuessrChatBotUseCase
{
    public GeoGuessrChatBotUseCase(ILogger<GeoGuessrChatBotUseCase> logger, 
        ISelfUserAccess selfUserAccess, 
        IMetaVectorStoreSearchPlugin  metaVectorStoreSearchPlugin,
        IConfiguration config)
    {
        _logger = logger;
        _selfUserAccess = selfUserAccess;

        var llmEndpoint = config.GetConnectionString(ConfigKeys.LlmInferenceEndpointConnectionString)!;
        var llmModelName = config.GetValue<string>(ConfigKeys.LlmModelNameConfigurationKey)!;
        var embeddingModelName = config.GetValue<string>(ConfigKeys.EmbeddingModelNameConfigurationKey)!;
        var embeddingEndpoint = config.GetConnectionString(ConfigKeys.EmbeddingEndpoint)!;
        var llmApiKey = config.GetValue<string>(ConfigKeys.LlmApiKeyConfigurationKey);

        _logger.LogInformation("LLM Endpoint: {Endpoint}", llmEndpoint);
        _logger.LogInformation("LLM Model: {Model}", llmModelName);
        _logger.LogInformation("Embedding Endpoint: {Endpoint}", embeddingEndpoint);
        _logger.LogInformation("Embedding Model: {Model}", embeddingModelName);

        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: llmModelName, apiKey: llmApiKey, endpoint: new Uri(llmEndpoint));
        _kernel = kernelBuilder.Build();

        _kernel.Plugins
            .AddFromObject(metaVectorStoreSearchPlugin, "MetasDatabase");
    }
    
    public async Task<string?> GetAiResponseAsync(string prompt, Func<Task> startTypingAsync)
    {
        try
        {
            _logger.LogDebug($"Handling message using AI: {prompt}");

            // Get the message
            var message = prompt.Replace($"<@{_selfUserAccess.GetSelfUserId()}>", "@DRAGON");
            ChatHistory history = [];
            history.AddSystemMessage(
                "You are **DRAGON**, a helpful GeoGuessr assistant Discord bot. " +
                "Users will mention you with **@DRAGON**. When you choose to use functions, always give an explanation at the end." +

                "### Formatting Rules\n" +
                "- You may use **simplified Markdown syntax** for your responses.\n" +
                "- Allowed: headings, bullet lists, numbered lists, **bold**, *italic*, __underline__, subtext, masked links, code blocks, and block quotes.\n" +
                "- Forbidden: tables, horizontal dividers, or any attempt to imitate tables using symbols or spacing.\n" +

                "### Domain Knowledge\n" +
                "In GeoGuessr, a **meta** originally referred to identifying information visible only in Google Street View imagery — such as the car color used for image capture — that is not observable in real life. " +
                "Today, however, players often use the term *meta* more broadly to describe any clue or pattern that helps identify a location.\n" +

                "### Resources\n" +
                "You have access to the **MetasDatabase**, a trusted source of GeoGuessr metas for specific countries and territories.\n" +
                "- If a user asks about *meta-related* topics, **consult the MetasDatabase first**.\n" +
                "- If the user asks about a country or region, you can use the GetInformationByCountry function to get all the information that is known about the country.\n" +
                "- You can use the GetCountries function to get a list of all countries and regions that are in the database if you are unsure if a country is in the database.\n" +
                "- If the user asks about anything else, you can use the SearchInformation function to search for an arbitrary text.\n" +
                "- If a specific territory does not have its own entry, check the corresponding country’s entry instead — these often include relevant information.\n" +

                "### Source Attribution\n" +
                "Always cite your sources as **clickable links** (masked Markdown links). masked Markdown links look like this: [text](https://www.example.com)"
            );
            history.AddUserMessage(message);
            
            OpenAIPromptExecutionSettings promtExecutionSettings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };

            var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
            await startTypingAsync().ConfigureAwait(false);
            var response = await chatSvc.GetChatMessageContentAsync(history, promtExecutionSettings, _kernel)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogError("LLM failed to respond.");
                return null;
            }

            _logger.LogDebug($"Handling done.");
            return response.Content;
        }
        catch (HttpOperationException httpEx) when(httpEx.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogError(httpEx, "Too many requests have been reached.");
            return "AI is currently not available. Try again later.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI response");
        }

        return null;
    }
    
    private readonly Kernel _kernel;
    private readonly ISelfUserAccess _selfUserAccess;
    private readonly ILogger<GeoGuessrChatBotUseCase> _logger;
}