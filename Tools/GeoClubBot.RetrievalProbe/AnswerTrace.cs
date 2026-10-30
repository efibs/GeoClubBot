using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UseCases.OutputPorts.AI;
using Utilities;

namespace GeoClubBot.RetrievalProbe;

/// <summary>One chat request the answer made, as the report lists it.</summary>
/// <param name="Chain">The models named, best first.</param>
/// <param name="ToolsOffered">How many tools the request declared; 0 for a call that has to answer.</param>
/// <param name="ToolChoice">"auto", or "none" when the call had to answer with the tools still declared.</param>
/// <param name="ToolCalls">The tools the reply asked for, by name.</param>
/// <param name="Error">The failure's code, when the request failed.</param>
public sealed record ChatCallRecord(
    IReadOnlyList<string> Chain,
    int ToolsOffered,
    string ToolChoice,
    string? ModelUsed,
    string? FinishReason,
    int TextLength,
    IReadOnlyList<string> ToolCalls,
    int PromptTokens,
    int CompletionTokens,
    double Seconds,
    string? Error);

/// <summary>
/// What happened while one rated answer was asked again: its chat requests, the prompt that answered,
/// and what the bot logged. Held in an async-local, like <see cref="RequestAttribution"/>, so answers
/// asked side by side keep theirs apart.
/// </summary>
public sealed class AnswerTrace
{
    private static readonly AsyncLocal<AnswerTrace?> CurrentTrace = new();

    private readonly List<ChatCallRecord> _calls = [];
    private readonly List<string> _log = [];

    public static AnswerTrace? Current
    {
        get => CurrentTrace.Value;
        set => CurrentTrace.Value = value;
    }

    public IReadOnlyList<ChatCallRecord> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_log)
            {
                return [.. _log];
            }
        }
    }

    /// <summary>The messages of the last request that succeeded: what the answer was written from.</summary>
    public IReadOnlyList<AiChatMessage>? LastMessages { get; private set; }

    /// <summary>
    /// The text of the last reply that answered rather than called tools, as the model wrote it: its [n]
    /// are the excerpts' own numbers, before <c>CitationResolver</c> renumbers them for the reader.
    /// </summary>
    public string? LastAnswerText { get; private set; }

    public void Record(ChatCallRecord call, IReadOnlyList<AiChatMessage> messages, string? answerText)
    {
        lock (_calls)
        {
            _calls.Add(call);
            if (call.Error is null)
            {
                LastMessages = messages;
            }

            if (call.Error is null && call.ToolCalls.Count == 0 && answerText is not null)
            {
                LastAnswerText = answerText;
            }
        }
    }

    public void Write(string line)
    {
        lock (_log)
        {
            _log.Add(line);
        }
    }
}

/// <summary>The bot's own chat client, every request it makes recorded on the answer it was made for.</summary>
public sealed class RecordingChatModelClient(IChatModelClient inner) : IChatModelClient
{
    public async Task<Result<AiChatResponse>> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;

        var response = result.IsSuccess ? result.Value : null;
        AnswerTrace.Current?.Record(
            new ChatCallRecord(
                request.ModelChain,
                request.Tools?.Count ?? 0,
                request.Tools is { Count: > 0 } ? request.ToolChoice == AiToolChoice.None ? "none" : "auto" : "-",
                response?.ModelUsed,
                response?.FinishReason,
                response?.Text.Length ?? 0,
                [.. response?.ToolCalls?.Select(call => call.Name) ?? []],
                response?.Usage.PromptTokens ?? 0,
                response?.Usage.CompletionTokens ?? 0,
                seconds,
                result.IsFailure ? result.Error.Code : null),
            request.Messages,
            response?.Text);

        return result;
    }

    public Task<Result<IReadOnlyList<ChatModelDescriptor>>> ReadFreeModelsAsync(CancellationToken cancellationToken = default) =>
        inner.ReadFreeModelsAsync(cancellationToken);

    public Task<Result<ChatModelHealth>> ReadModelHealthAsync(string modelId, CancellationToken cancellationToken = default) =>
        inner.ReadModelHealthAsync(modelId, cancellationToken);
}

/// <summary>
/// The bot's log lines, kept with the answer they belong to. Information and above from everything,
/// debug from the answering code too — which wording it searched by, what it carried — since that is
/// what explains an answer. Warnings and errors outside an answer go to the console.
/// </summary>
public sealed class AnswerTraceLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TraceLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class TraceLogger(string category) : ILogger
    {
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];
        private readonly bool _isAnswering = category.StartsWith("UseCases.UseCases.AI", StringComparison.Ordinal);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= (_isAnswering ? LogLevel.Debug : LogLevel.Information);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{Level(logLevel)} {_shortCategory}: {formatter(state, exception)}"
                       + (exception is null ? string.Empty : $" ({exception.GetType().Name}: {exception.Message})");

            if (AnswerTrace.Current is { } trace)
            {
                trace.Write(line);
            }
            else if (logLevel >= LogLevel.Warning)
            {
                Console.Error.WriteLine(line);
            }
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "trce"
        };
    }
}
