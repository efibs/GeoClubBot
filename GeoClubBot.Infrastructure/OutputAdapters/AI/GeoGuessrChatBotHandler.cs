using System.Net;
using Configuration;
using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.AI;

namespace Infrastructure.OutputAdapters.AI;

public partial class GeoGuessrChatBotHandler : IRequestHandler<GetAiResponseQuery, string?>
{
    private const string SystemName = "Dragon";
    private const string AvailableCountriesPlaceholder = "{{AvailableCountries}}";

    private const string SystemPrompt = $@"
You are **{SystemName}**, a helpful GeoGuessr assistant Discord bot.
Users will mention you with **@{SystemName}**. When you choose to use functions, always give an explanation at the end.

### Formatting Rules
- You may use **simplified Markdown syntax** for your responses.
- Allowed: headings, bullet lists, numbered lists, **bold**, *italic*, __underline__, subtext, masked links, code blocks, and block quotes.
- Forbidden: tables, horizontal dividers, or any attempt to imitate tables using symbols or spacing.

### Domain Knowledge
In GeoGuessr, a **meta** originally referred to identifying information visible only in Google Street View imagery — such as the car color used for image capture — that is not observable in real life.
Today, however, players often use the term *meta* more broadly to describe any clue or pattern that helps identify a location.

Regionguessing refers to guessing the specific region inside a larger country. For example an regionguessing clue for russia is that in the south of the country the gen 4 coverage will be in winter.

### Resources
You have access to the **PlonkIt Guide**, a trusted source of GeoGuessr metas for specific countries and territories.
- If a user asks about *meta-related* topics, **consult the PlonkIt Guide first**.
- If the user asks about a country or region, you can use the GetInformationByCountry function to get all the information that is known about the country.
- Here is the list of available countries (case sensitive!): {AvailableCountriesPlaceholder}
- If the user asks about anything else, you can use the SearchInformation function to search for an arbitrary text.
- You don't have to include the word 'meta' in your search queries.
- If a specific territory does not have its own entry, check the corresponding country’s entry instead — these often include relevant information.
- If you don't find any information on the requested topic in the PlonkIt Guide, say that you couldn't find any information on that. Don't try to rely on anything else that you have learned, it will be wrong.

### Source Attribution
Always cite your sources as **clickable links** (masked Markdown links). masked Markdown links look like this: [text](https://www.example.com)
";

    public GeoGuessrChatBotHandler(
        ILogger<GeoGuessrChatBotHandler> logger,
        IDiscordSelfUserAccess discordSelfUserAccess,
        PlonkItGuidePlugin plonkItGuidePlugIn,
        IConfiguration config,
        IOptions<AiConfiguration> aiOptions)
    {
        _logger = logger;
        _discordSelfUserAccess = discordSelfUserAccess;
        _plonkItGuidePlugIn = plonkItGuidePlugIn;

        var aiConfig = aiOptions.Value;

        var llmEndpoint = config.GetConnectionString(ConfigKeys.LlmInferenceEndpointConnectionString)!;
        var llmModelName = aiConfig.LlmModel!;
        var embeddingModelName = aiConfig.EmbeddingModel!;
        var embeddingEndpoint = config.GetConnectionString(ConfigKeys.EmbeddingEndpoint)!;
        var llmApiKey = aiConfig.LlmApiKey;

        LogLlmEndpoint(llmEndpoint);
        LogLlmModel(llmModelName);
        LogEmbeddingEndpoint(embeddingEndpoint);
        LogEmbeddingModel(embeddingModelName);

        _requestTimeout = TimeSpan.FromSeconds(aiConfig.RequestTimeoutSeconds);
        _overallTimeout = TimeSpan.FromSeconds(aiConfig.OverallTimeoutSeconds);

        var httpClient = new HttpClient(new RateLimitRetryHandler(new HttpClientHandler()))
        {
            Timeout = _requestTimeout
        };
        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId: llmModelName, apiKey: llmApiKey, endpoint: new Uri(llmEndpoint + "/v1"), httpClient: httpClient);
        _kernel = kernelBuilder.Build();

        _kernel.Plugins
            .AddFromObject(plonkItGuidePlugIn, "PlonkIt_Guide");
    }

    public async Task<string?> Handle(GetAiResponseQuery request, CancellationToken cancellationToken)
    {
        var semaphoreClaimed = await _plonkItGuidePlugIn
            .RebuildStoreLock
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        if (semaphoreClaimed == false)
        {
            return "The internal PlonkIt Guide is currently being updated. Try again later.";
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_overallTimeout);

        try
        {
            LogHandlingMessageUsingAiPrompt(request.Prompt);

            var message = request.Prompt.Replace($"<@{_discordSelfUserAccess.GetSelfUserId()}>", $"@{SystemName}");

            var availableCountries = await _plonkItGuidePlugIn.GetCountries().ConfigureAwait(false);
            var formattedSystemPrompt = SystemPrompt.Replace(AvailableCountriesPlaceholder, availableCountries);

            ChatHistory history = [];
            history.AddSystemMessage(formattedSystemPrompt);
            history.AddUserMessage(message);

            OpenAIPromptExecutionSettings promptExecutionSettings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.1
            };

            var chatSvc = _kernel.GetRequiredService<IChatCompletionService>();
            await request.StartTypingAsync().ConfigureAwait(false);
            var response = await chatSvc.GetChatMessageContentAsync(history, promptExecutionSettings, _kernel, cts.Token)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                LogLlmEmptyResponse(_logger);
                return null;
            }

            LogAiHandlingDone(_logger);
            return response.Content;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogAiOverallTimeout(_logger, _overallTimeout.TotalSeconds);
            return $"AI response timed out (overall limit of {_overallTimeout.TotalSeconds}s reached). Try again later.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogAiPerRequestTimeout(_logger, _requestTimeout.TotalSeconds);
            return $"AI response timed out (request limit of {_requestTimeout.TotalSeconds}s reached). Try again later.";
        }
        catch (HttpOperationException httpEx) when (httpEx.StatusCode == HttpStatusCode.TooManyRequests)
        {
            LogAiTooManyRequests(_logger, httpEx);
            return "AI is currently not available. Try again later.";
        }
        catch (Exception ex)
        {
            LogAiUnexpectedError(_logger, ex);
        }
        finally
        {
            _plonkItGuidePlugIn.RebuildStoreLock.Release();
        }

        return null;
    }

    private readonly Kernel _kernel;
    private readonly IDiscordSelfUserAccess _discordSelfUserAccess;
    private readonly ILogger<GeoGuessrChatBotHandler> _logger;
    private readonly PlonkItGuidePlugin _plonkItGuidePlugIn;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _overallTimeout;

    [LoggerMessage(LogLevel.Information, "LLM Endpoint: {endpoint}")]
    partial void LogLlmEndpoint(string endpoint);

    [LoggerMessage(LogLevel.Information, "LLM Model: {model}")]
    partial void LogLlmModel(string model);

    [LoggerMessage(LogLevel.Information, "Embedding Endpoint: {endpoint}")]
    partial void LogEmbeddingEndpoint(string endpoint);

    [LoggerMessage(LogLevel.Information, "Embedding Model: {model}")]
    partial void LogEmbeddingModel(string model);

    [LoggerMessage(LogLevel.Debug, "Handling message using AI: {prompt}")]
    partial void LogHandlingMessageUsingAiPrompt(string prompt);

    [LoggerMessage(LogLevel.Error, "LLM failed to respond.")]
    static partial void LogLlmEmptyResponse(ILogger logger);

    [LoggerMessage(LogLevel.Debug, "Handling done.")]
    static partial void LogAiHandlingDone(ILogger logger);

    [LoggerMessage(LogLevel.Error, "AI overall timeout of {Timeout}s reached.")]
    static partial void LogAiOverallTimeout(ILogger logger, double timeout);

    [LoggerMessage(LogLevel.Error, "AI per-request timeout of {Timeout}s reached.")]
    static partial void LogAiPerRequestTimeout(ILogger logger, double timeout);

    [LoggerMessage(LogLevel.Error, "Too many requests have been reached.")]
    static partial void LogAiTooManyRequests(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Error during AI response")]
    static partial void LogAiUnexpectedError(ILogger logger, Exception ex);

    private sealed class RateLimitRetryHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private const int MaxRetries = 5;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync();

            HttpResponseMessage response = null!;

            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    var delay = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                }

                response = await base.SendAsync(request, cancellationToken);

                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    return response;
            }

            return response;
        }
    }
}
