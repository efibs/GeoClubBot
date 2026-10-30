using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Configuration;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;

namespace GeoClubBot.RetrievalProbe;

/// <summary>A planner reply as it came back, and how long it took.</summary>
/// <param name="Reply">The model's text, or null when the request failed.</param>
/// <param name="Seconds">How long the request took, to judge it against the bot's own timeout.</param>
public sealed record PlannerReply(string? Model, string? Reply, double Seconds, string? Error = null);

/// <summary>
/// Asks the query planner the bot asks — same prompt, same reply reader, a model chain picked by the
/// bot's own selector — and keeps every reply. A chat request is the bot's real cost, so this exists
/// only when the probe was started with <c>--allow-chat</c>, and a rerun costs nothing.
///
/// Replies are kept with how long they took rather than cut off at the bot's timeout, so one run tells
/// both what the planner gives and whether the bot would have waited for it.
/// </summary>
public sealed class PlannerCache : IDisposable
{
    /// <summary>Long enough to learn how slow a slow model is; the bot's own timeout is applied when a reply is used.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(150);

    private readonly IChatModelClient _client;
    private readonly OpenRouterConfiguration _openRouter;
    private readonly string _path;
    private readonly Func<int> _requestsSent;
    private readonly IDisposable? _owned;
    private readonly Dictionary<string, PlannerReply> _replies;
    private readonly IReadOnlyList<string> _models;
    private readonly SemaphoreSlim _write = new(1, 1);
    private IReadOnlyList<string>? _chain;

    /// <param name="models">A chain to ask instead of the bot's own; its replies are cached apart from the bot chain's.</param>
    public PlannerCache(
        IChatModelClient client,
        OpenRouterConfiguration openRouter,
        string path,
        Func<int>? requestsSent = null,
        IDisposable? owned = null,
        IReadOnlyList<string>? models = null)
    {
        _client = client;
        _openRouter = openRouter;
        _path = path;
        _requestsSent = requestsSent ?? (() => 0);
        _owned = owned;
        _models = models ?? [];
        _replies = Load(path);
    }

    /// <summary>
    /// The bot's <see cref="RefitChatModelClient"/> behind an <see cref="EmbeddingsOnlyHandler"/> that
    /// lets chat completions and the model roster through — and nothing else.
    /// </summary>
    public static PlannerCache ForOpenRouter(
        string apiKey,
        OpenRouterConfiguration openRouter,
        string path,
        IReadOnlyList<string>? models = null)
    {
        var guard = new EmbeddingsOnlyHandler(new HttpClientHandler(), allowChat: true);
        var httpClient = new HttpClient(guard) { BaseAddress = new Uri(openRouter.BaseUrl), Timeout = ProbeTimeout };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.Add("X-Title", $"{openRouter.AppName} retrieval probe");

        var client = new RefitChatModelClient(new SingleClientFactory(httpClient), NullLogger<RefitChatModelClient>.Instance);
        return new PlannerCache(client, openRouter, path, () => guard.RequestsSent, httpClient, models);
    }

    /// <summary>Requests this run has sent, the roster read included. Zero when every reply came from the cache.</summary>
    public int RequestsSent => _requestsSent();

    public string CachePath => _path;

    /// <summary>The planner's reply for this question, from the cache or asked now.</summary>
    public async Task<PlannerReply> AskAsync(
        IReadOnlyList<ConversationTurnView> turns,
        string question,
        bool hasScreenshot,
        CancellationToken cancellationToken)
    {
        var messages = RetrievalQueryPlanner.BuildMessages(turns, question, hasScreenshot);
        var key = Key(messages, _models);

        lock (_replies)
        {
            if (_replies.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var chain = await ChainAsync(cancellationToken).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        var completion = await _client
            .CompleteAsync(new AiChatRequest(chain, messages, Temperature: RetrievalQueryPlanner.Temperature), cancellationToken)
            .ConfigureAwait(false);
        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

        var reply = completion.IsSuccess
            ? new PlannerReply(completion.Value.ModelUsed, completion.Value.Text, seconds)
            : new PlannerReply(null, null, seconds, completion.Error.Code);

        Console.Error.WriteLine(
            $"Planner: {(reply.Reply is null ? $"failed ({reply.Error})" : reply.Model)} in {seconds:0.0} s - {question}");

        // A failure is not kept: it says something about the moment, not the question.
        if (reply.Reply is not null)
        {
            string serialised;
            lock (_replies)
            {
                _replies[key] = reply;
                serialised = JsonSerializer.Serialize(_replies);
            }

            await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.WriteAllTextAsync(_path, serialised, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _write.Release();
            }
        }

        return reply;
    }

    /// <summary>The chain asked: the one named on the command line, or the bot's own, read once.</summary>
    private async Task<IReadOnlyList<string>> ChainAsync(CancellationToken cancellationToken)
    {
        if (_models.Count > 0)
        {
            return _models;
        }

        await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _chain ??= await ReadChainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _write.Release();
        }
    }

    public void Dispose()
    {
        _write.Dispose();
        _owned?.Dispose();
    }

    /// <summary>The chain the bot would name for a text-only request today, by its own selector.</summary>
    private async Task<IReadOnlyList<string>> ReadChainAsync(CancellationToken cancellationToken)
    {
        var roster = await _client.ReadFreeModelsAsync(cancellationToken).ConfigureAwait(false);
        if (roster.IsFailure)
        {
            throw new ProbeException($"The model roster could not be read: {roster.Error.Message}");
        }

        var chain = ChatModelSelector.SelectChain(
            roster.Value,
            new ChatModelRequirements(MinContextLength: _openRouter.MinContextLength),
            new ChatModelSelectionOptions
            {
                PreferredModelPrefixes = _openRouter.PreferredModelPrefixes,
                BlockedModelIds = _openRouter.BlockedModelIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
                FallbackModelId = _openRouter.FallbackModelId,
                ChainLength = Math.Clamp(_openRouter.ChainLength, 1, 3),
                ExpiryHorizon = TimeSpan.FromHours(_openRouter.ExpiryHorizonHours)
            },
            failurePenalties: null,
            DateTimeOffset.UtcNow);

        Console.Error.WriteLine($"Planner chain: {string.Join(", ", chain)}");
        return chain;
    }

    /// <summary>
    /// Keyed by what is sent, not by the model that answered: the cached reply stands for the question
    /// however the roster changes. A chain named with <c>--planner-models</c> is part of the key, so its
    /// replies never stand in for the bot's own chain's, or the other way round.
    /// </summary>
    private static string Key(IReadOnlyList<AiChatMessage> messages, IReadOnlyList<string> models) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            (models.Count > 0 ? string.Join(",", models) + "\u001D" : string.Empty)
            + string.Join("\u001E", messages.Select(message => $"{message.Role}\u001F{message.ToPlainText()}")))));

    private static Dictionary<string, PlannerReply> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, PlannerReply>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, PlannerReply>>(File.ReadAllText(path))
                   ?? new Dictionary<string, PlannerReply>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, PlannerReply>(StringComparer.Ordinal);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
