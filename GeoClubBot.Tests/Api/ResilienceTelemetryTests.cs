using System.Net;
using FluentAssertions;
using GeoClubBot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Pins what the operator's alert channel is told. Logs from Warning up are forwarded to Discord, and
/// Polly reports a timeout at Error — so every slow provider call the pipeline then handled arrived
/// there as a failure someone had to look at, beside the one line the caller writes when a call really
/// fails. Driven through the real HTTP resilience handler, because whether the severity setting is
/// honoured at all is exactly what is in question.
/// </summary>
public sealed class ResilienceTelemetryTests
{
    [Fact]
    public async Task ATimeoutThePipelineHandled_IsNotReportedAsAnError() =>
        await AssertHandledQuietly(
            "OnTimeout",
            builder => builder.AddTimeout(TimeSpan.FromMilliseconds(50)),
            () => new NeverAnsweringHandler());

    [Fact]
    public async Task ARetryThePipelineMade_IsNotReportedAsAWarning() =>
        // Same reasoning as the timeout: a retry is the pipeline working, and the caller is what says
        // whether the call ended up failing.
        await AssertHandledQuietly(
            "OnRetry",
            builder => builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant
            }),
            () => new FailingHandler());

    /// <summary>
    /// Drives a real HTTP resilience handler until <paramref name="eventName"/> is reported, and
    /// asserts it reached the log below Warning — whether the severity setting is honoured through
    /// <c>AddResilienceHandler</c> at all being exactly what is in question.
    /// </summary>
    private static async Task AssertHandledQuietly(
        string eventName,
        Action<ResiliencePipelineBuilder<HttpResponseMessage>> configure,
        Func<HttpMessageHandler> handler)
    {
        var recorder = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddResilienceTelemetryDefaults();
        services.AddLogging(logging => logging.AddProvider(recorder).SetMinimumLevel(LogLevel.Trace));
        services.AddHttpClient("subject")
            .ConfigurePrimaryHttpMessageHandler(handler)
            .AddResilienceHandler("TestPipeline", configure);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("subject");

        try
        {
            using var response = await client.GetAsync("https://example.invalid/subject");
        }
        catch (Exception)
        {
            // The caller still learns what happened; this test is only about what was logged.
        }

        // Matched on the event Polly names in the message, so an unrelated event of its own is not
        // what this test passes or fails on.
        var events = recorder.Entries
            .Where(entry => entry.Category == "Polly" && entry.Message.Contains(eventName))
            .ToList();

        events.Should().NotBeEmpty($"{eventName} is still reported, one level down");
        events.Should().OnlyContain(entry => entry.Level < LogLevel.Warning);
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Record);

        public void Dispose()
        {
        }

        private void Record(LogEntry entry)
        {
            lock (_entries)
            {
                _entries.Add(entry);
            }
        }
    }

    private sealed class RecordingLogger(string category, Action<LogEntry> record) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            record(new LogEntry(category, logLevel, formatter(state, exception)));
    }

    /// <summary>Answers with a status the pipeline retries.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    /// <summary>Answers only when cancelled, so the pipeline's timeout is what ends the attempt.</summary>
    private sealed class NeverAnsweringHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
