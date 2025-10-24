using System.Net;
using Constants;
using Discord;
using Discord.WebSocket;
using Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Qdrant.Client;

namespace GeoClubBot.Services;

/// <summary>
/// Class managing the discord socket
/// </summary>
/// <param name="client">The discord socket client</param>
public class DiscordBotService : IHostedService
{
    public DiscordBotService(DiscordSocketClient client, IConfiguration config, ILogger<DiscordBotService> logger,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _config = config;
        _logger = logger;

        var qdrantEndpoint = config.GetConnectionString(ConfigKeys.QDrantConnectionString)!;
        var qdrantClient = new QdrantClient(qdrantEndpoint);

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

        var embeddingService = new VllmEmbeddingService(new Uri(embeddingEndpoint), embeddingModelName);
        
        _metaVectorStore = new MetaVectorStore(
            qdrantClient, 
            embeddingService,
            loggerFactory.CreateLogger<MetaVectorStore>());

        _kernel.Plugins
            .AddFromObject(new MetaVectorStoreSearchPlugin(
                _metaVectorStore, loggerFactory.CreateLogger<MetaVectorStoreSearchPlugin>()), 
                "MetasDatabase");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the token from the configuration
        var token = _config.GetValue<string>(ConfigKeys.DiscordBotTokenConfigurationKey);

        // If the token was not given
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Discord Bot token not set.");
        }

        // Login the bot
        await _client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);

        // Start the bot
        await _client.StartAsync().ConfigureAwait(false);

        await _metaVectorStore.InitializeAsync().ConfigureAwait(false);
        
        _client.MessageReceived += _onMessageReceived;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do here
        return Task.CompletedTask;
    }

    private Task _onMessageReceived(SocketMessage socketMessage)
    {
        if (socketMessage.MentionedUserIds.Contains(_client.CurrentUser.Id) == false)
        {
            return Task.CompletedTask;
        }

        Task.Run(async () => await _handleMessageAsync(socketMessage).ConfigureAwait(false));
        
        return Task.CompletedTask;
    }

    private async Task _handleMessageAsync(SocketMessage socketMessage)
    {
        try
        {
            _logger.LogDebug($"Handling message using AI: {socketMessage.Content}");

            // Get the message
            var message = socketMessage.Content.Replace($"<@{_client.CurrentUser.Id}>", "@DRAGON");
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
                "Always cite your sources as **clickable links** (masked Markdown links)."
            );
            history.AddUserMessage(message);

            OpenAIPromptExecutionSettings promtExecutionSettings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };

            var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
            await socketMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);
            var response = await chatSvc.GetChatMessageContentAsync(history, promtExecutionSettings, _kernel)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogError("LLM failed to respond.");
                return;
            }

            // For every split
            foreach (var substring in response.Content.SplitAtCharWithLimit("\n", 2000))
            {
                await socketMessage.Channel.SendMessageAsync(substring).ConfigureAwait(false);
            }

            _logger.LogDebug($"Handling done.");
        }
        catch (HttpOperationException httpEx) when(httpEx.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await socketMessage.Channel.SendMessageAsync("AI is currently not available. Try again later.").ConfigureAwait(false);
            _logger.LogError(httpEx, "Too many requests have been reached.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI response");
        }
    }
    

    
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly Kernel _kernel;
    private readonly MetaVectorStore _metaVectorStore;
    private readonly ILogger<DiscordBotService> _logger;
    
    
}