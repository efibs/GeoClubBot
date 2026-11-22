using System.Security.Cryptography;
using System.Text;
using Configuration;
using Infrastructure.OutputAdapters.AI.OpenRouter.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Refit;

namespace Infrastructure.OutputAdapters.AI.OpenRouter;

/// <summary>An embedding model in use: its id, the width of its vectors and the collection holding them.</summary>
public sealed record EmbeddingModelChoice(string ModelId, int Dimensions, string CollectionName)
{
    /// <summary>
    /// The collection for a model and width. Both are in the name, so a change lands on a new collection
    /// instead of mixing vectors that cannot be compared into the old one, and switching back finds the
    /// old one again.
    /// </summary>
    public static string CollectionNameFor(string prefix, string modelId, int dimensions)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelId)))[..8]
            .ToLowerInvariant();

        return $"{prefix}-{fingerprint}-{dimensions}";
    }
}

/// <summary>
/// Picks the embedding model, and moves to another when OpenRouter stops serving it.
///
/// Free embedding models are withdrawn without notice: the first default answered 404 "No endpoints
/// found" for every request from 2026-11-16, which left every question and every ingestion run failing
/// until the configuration changed (#503). The order of preference is the configured model, then the
/// configured fallbacks, then (when allowed) any free model in OpenRouter's embedding catalog that takes
/// both text and images. A model is passed over when the catalog leaves it out or when it answers 404,
/// and stays passed over until restart.
///
/// Vectors from different models cannot be compared, so each model has its own collection
/// (<see cref="EmbeddingModelChoice.CollectionNameFor"/>). A switch starts on that collection, and
/// ingestion indexes every guide again into it, because each source records the model it was indexed with.
/// </summary>
public sealed partial class EmbeddingModelSelector(
    IHttpClientFactory httpClientFactory,
    IOptions<AiConfiguration> configuration,
    ILogger<EmbeddingModelSelector> logger) : IKnowledgeCollectionSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _learnedDimensions = new(StringComparer.OrdinalIgnoreCase);
    private volatile EmbeddingModelChoice? _current;
    private bool _fixed;

    /// <summary>
    /// A selector that keeps the configured model and never reads the catalog or switches: for the
    /// read-only probe, which measures one given index, and for tests of the embedder alone.
    /// </summary>
    public static EmbeddingModelSelector Fixed(IHttpClientFactory httpClientFactory, IOptions<AiConfiguration> configuration)
    {
        var selector = new EmbeddingModelSelector(httpClientFactory, configuration, NullLogger<EmbeddingModelSelector>.Instance);
        selector._fixed = true;
        selector._current = selector.Configured();
        return selector;
    }

    /// <summary>The model in use; before anything has resolved it, the configured one.</summary>
    public EmbeddingModelChoice Current => _current ?? Configured();

    public string CollectionName => Current.CollectionName;

    public int VectorSize => Current.Dimensions;

    public async Task ResolveAsync(CancellationToken cancellationToken = default) =>
        await GetAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The model to embed with. Chosen on first use, with one read of OpenRouter's embedding catalog;
    /// when no model can be had, the configured one, so its requests fail with the provider's own reason.
    /// </summary>
    public async Task<EmbeddingModelChoice> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_current is { } current)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is { } chosen)
            {
                return chosen;
            }

            var choice = await ChooseAsync(cancellationToken).ConfigureAwait(false);
            if (choice is null)
            {
                LogNoModelAvailable(logger);
                return Configured();
            }

            LogChosen(logger, choice.ModelId, choice.Dimensions, choice.CollectionName);
            _current = choice;
            return choice;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Records that OpenRouter no longer serves <paramref name="modelId"/> and moves to the next model.
    /// </summary>
    /// <returns>The model now in use, or null when there is none to move to.</returns>
    public async Task<EmbeddingModelChoice?> ReportGoneAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (_fixed)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is { } current && !string.Equals(current.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                // Another request has already moved on.
                return current;
            }

            if (_gone.Add(modelId))
            {
                LogGone(logger, modelId);
            }

            var next = await ChooseAsync(cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                LogNoModelAvailable(logger);
                return null;
            }

            LogSwitched(logger, modelId, next.ModelId, next.Dimensions, next.CollectionName);
            _current = next;
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private EmbeddingModelChoice Configured()
    {
        var ai = configuration.Value;
        return new EmbeddingModelChoice(
            ai.OpenRouter.EmbeddingModelId,
            ai.OpenRouter.EmbeddingDimensions,
            EmbeddingModelChoice.CollectionNameFor(
                ai.KnowledgeCollectionPrefix, ai.OpenRouter.EmbeddingModelId, ai.OpenRouter.EmbeddingDimensions));
    }

    private async Task<EmbeddingModelChoice?> ChooseAsync(CancellationToken cancellationToken)
    {
        var ai = configuration.Value;
        var openRouter = ai.OpenRouter;

        // A failed read leaves the catalog out of the decision rather than blocking every model.
        var catalog = await ReadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var served = catalog?.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configured = new List<(string ModelId, int Dimensions)> { (openRouter.EmbeddingModelId, openRouter.EmbeddingDimensions) };
        configured.AddRange(openRouter.FallbackEmbeddingModels.Select(option => (option.ModelId, option.Dimensions)));

        var listed = configured
            .Where(option => !string.IsNullOrWhiteSpace(option.ModelId))
            .Select(option => (ModelId: option.ModelId.Trim(), option.Dimensions))
            // Configuration binding appends a configured list to the defaults, so a model can appear twice.
            .DistinctBy(option => option.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (modelId, dimensions) in listed)
        {
            if (_gone.Contains(modelId))
            {
                continue;
            }

            if (served is not null && !served.Contains(modelId))
            {
                LogNotInCatalog(logger, modelId);
                continue;
            }

            if (await ChoiceForAsync(modelId, dimensions, ai.KnowledgeCollectionPrefix, cancellationToken)
                    .ConfigureAwait(false) is { } choice)
            {
                return choice;
            }
        }

        if (!openRouter.AutoSelectEmbeddingModel || catalog is null)
        {
            return null;
        }

        var listedIds = listed.Select(option => option.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in catalog.Where(IsFreeTextAndImageModel))
        {
            if (_gone.Contains(model.Id) || listedIds.Contains(model.Id))
            {
                continue;
            }

            if (await ChoiceForAsync(model.Id, 0, ai.KnowledgeCollectionPrefix, cancellationToken)
                    .ConfigureAwait(false) is { } choice)
            {
                LogAutoSelected(logger, model.Id);
                return choice;
            }
        }

        return null;
    }

    /// <summary>
    /// Text and images share one space in the index (a picture is found by the question's words), so a
    /// model taking only text would leave every picture without its vector.
    /// </summary>
    private static bool IsFreeTextAndImageModel(OpenRouterModelDto model) =>
        model.Pricing?.Prompt is "0"
        && model.Architecture?.InputModalities is { } inputs
        && inputs.Contains("text", StringComparer.OrdinalIgnoreCase)
        && inputs.Contains("image", StringComparer.OrdinalIgnoreCase);

    private async Task<EmbeddingModelChoice?> ChoiceForAsync(
        string modelId,
        int dimensions,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (dimensions <= 0)
        {
            var learned = await LearnDimensionsAsync(modelId, cancellationToken).ConfigureAwait(false);
            if (learned is not { } width)
            {
                return null;
            }

            dimensions = width;
        }

        return new EmbeddingModelChoice(modelId, dimensions, EmbeddingModelChoice.CollectionNameFor(prefix, modelId, dimensions));
    }

    private async Task<IReadOnlyList<OpenRouterModelDto>?> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await Api().ReadEmbeddingModelsAsync(cancellationToken).ConfigureAwait(false);

            // An empty list says more about the read than about OpenRouter: leave the catalog out.
            return response.Data is { Count: > 0 } models ? models : null;
        }
        catch (Exception ex) when (ex is ApiException or ApiRequestException or HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogCatalogUnreadable(logger, ex.Message, ex);
            return null;
        }
    }

    /// <summary>
    /// The width of a model's vectors, from one embedding of a few words. Remembered, so a model is
    /// measured once per process. A 404 marks the model gone; any other failure only skips it this time.
    /// </summary>
    private async Task<int?> LearnDimensionsAsync(string modelId, CancellationToken cancellationToken)
    {
        if (_learnedDimensions.TryGetValue(modelId, out var known))
        {
            return known;
        }

        var request = new OpenRouterEmbeddingRequestDto
        {
            Model = modelId,
            Input = [new OpenRouterEmbeddingInputDto { Content = [new OpenRouterContentPartDto { Type = "text", Text = "dimension probe" }] }]
        };

        try
        {
            var response = await Api().CreateEmbeddingsAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error is { Code: 404 })
            {
                _gone.Add(modelId);
                LogGone(logger, modelId);
                return null;
            }

            if (response.Data is [{ Embedding: { Length: > 0 } embedding }, ..])
            {
                _learnedDimensions[modelId] = embedding.Length;
                LogDimensionsLearned(logger, modelId, embedding.Length);
                return embedding.Length;
            }

            LogDimensionsUnlearned(logger, modelId, response.Error?.Message ?? "no vector returned", null);
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _gone.Add(modelId);
            LogGone(logger, modelId);
            return null;
        }
        catch (Exception ex) when (ex is ApiException or ApiRequestException or HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogDimensionsUnlearned(logger, modelId, ex.Message, ex);
            return null;
        }
    }

    private IOpenRouterApi Api() =>
        RestService.ForGenerated<IOpenRouterApi>(
            httpClientFactory.CreateClient(RefitChatModelClient.HttpClientName), OpenRouterJsonContext.Default);

    [LoggerMessage(LogLevel.Information, "Embedding with {ModelId} ({Dimensions} dimensions) into collection {Collection}.")]
    static partial void LogChosen(ILogger logger, string modelId, int dimensions, string collection);

    [LoggerMessage(LogLevel.Warning, "OpenRouter no longer serves the embedding model {ModelId}; it is passed over until restart.")]
    static partial void LogGone(ILogger logger, string modelId);

    [LoggerMessage(LogLevel.Warning,
        "Switched embedding model from {OldModelId} to {ModelId} ({Dimensions} dimensions). Collection {Collection} is used from now on, and every guide is indexed again into it over the following ingestion runs.")]
    static partial void LogSwitched(ILogger logger, string oldModelId, string modelId, int dimensions, string collection);

    [LoggerMessage(LogLevel.Warning, "No configured embedding model is served; picked {ModelId} from OpenRouter's embedding catalog.")]
    static partial void LogAutoSelected(ILogger logger, string modelId);

    [LoggerMessage(LogLevel.Warning, "The embedding model {ModelId} is not in OpenRouter's embedding catalog; trying the next one.")]
    static partial void LogNotInCatalog(ILogger logger, string modelId);

    [LoggerMessage(LogLevel.Error,
        "No embedding model can be used: the configured model and every fallback are gone, and no free OpenRouter model taking text and images could be picked. Set AI:OpenRouter:EmbeddingModelId to a model that is served.")]
    static partial void LogNoModelAvailable(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Could not read OpenRouter's embedding catalog ({Reason}); choosing among the configured models without it.")]
    static partial void LogCatalogUnreadable(ILogger logger, string reason, Exception exception);

    [LoggerMessage(LogLevel.Information, "The embedding model {ModelId} produces {Dimensions}-dimensional vectors.")]
    static partial void LogDimensionsLearned(ILogger logger, string modelId, int dimensions);

    [LoggerMessage(LogLevel.Warning, "Could not learn the vector width of {ModelId}: {Reason}")]
    static partial void LogDimensionsUnlearned(ILogger logger, string modelId, string reason, Exception? exception);
}
