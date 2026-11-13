using System.Globalization;
using System.Net;
using FluentAssertions;
using GeoClubBot.DependencyInjection;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UseCases.OutputPorts.AI;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Pins how the OpenRouter clients are wired, through the real registrations and resilience handlers:
/// the provider health reads a catalog refresh makes share the questions' rate limiter, and nothing else
/// of their pipeline. Through the chat pipeline, an endpoints route failing on its own opened the circuit
/// breaker by itself, and every question then failed for two minutes although chat was answering. And a
/// completion that opens and never ends is cut off, which nothing did before 2026-11-11.
/// </summary>
public sealed class OpenRouterHttpClientsTests
{
    [Fact]
    public async Task HealthReadsThatFail_DoNotOpenTheCircuitQuestionsGoThrough()
    {
        var upstream = new RoutingHandler(path => path.EndsWith("/endpoints", StringComparison.Ordinal)
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.OK);
        await using var provider = BuildProvider(upstream, requestsPerMinute: 6_000);
        var client = provider.GetRequiredService<IChatModelClient>();

        // A whole refresh's worth, and then some: the circuit opens after five failures in a minute.
        for (var read = 0; read < ChatModelCatalog.MaxHealthChecks; read++)
        {
            (await client.ReadModelHealthAsync($"a/model-{read}:free")).IsFailure.Should().BeTrue();
        }

        var answer = await client.CompleteAsync(new AiChatRequest(["a/model:free"], [AiChatMessage.User("hi")]));

        answer.IsSuccess.Should().BeTrue("a failing endpoints route says nothing about whether chat answers");
        upstream.Requests.Count(path => path.EndsWith("/endpoints", StringComparison.Ordinal))
            .Should().Be(ChatModelCatalog.MaxHealthChecks, "a failed read is not retried: it only leaves a model unmeasured");
    }

    [Fact]
    public async Task HealthReads_WaitForTheSameTokensAsQuestions()
    {
        // A budget of two leaves a single token, refilled once a minute. The question spends it, so a
        // health read sharing the limiter queues instead of going out.
        var upstream = new RoutingHandler(_ => HttpStatusCode.OK);
        await using var provider = BuildProvider(upstream, requestsPerMinute: 2);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var question = await factory.CreateClient(RefitChatModelClient.HttpClientName)
            .PostAsync(new Uri("/api/v1/chat/completions", UriKind.Relative), new StringContent(ChatBody));
        question.StatusCode.Should().Be(HttpStatusCode.OK);

        using var patience = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var healthRead = () => factory.CreateClient(RefitChatModelClient.HealthHttpClientName)
            .GetAsync(new Uri("/api/v1/models/a/model:free/endpoints", UriKind.Relative), patience.Token);

        await healthRead.Should().ThrowAsync<OperationCanceledException>("it is still waiting for the question's limiter");
        upstream.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ACompletionThatOpensAndNeverEnds_IsCutOff_AsTheModelsFailure_AndNotSentAgain()
    {
        // OpenRouter answers a completion at once and pads the response while the model works. The
        // client's timeout and the pipeline's attempt timeout both stop counting at the headers, so a
        // model that stalled held the answer for 57 minutes. A second for the reply here, in real time,
        // with the attempt and the call left at a minute and three: it is the registration that is
        // under test, with every handler the bot has.
        var upstream = new RoutingHandler(_ => HttpStatusCode.OK, () => new StalledBody());
        await using var provider = BuildProvider(upstream, requestsPerMinute: 6_000, responseTimeoutSeconds: 1);
        var client = provider.GetRequiredService<IChatModelClient>();

        var answer = await client.CompleteAsync(new AiChatRequest(["a/model:free"], [AiChatMessage.User("hi")]));

        answer.IsFailure.Should().BeTrue("the body would have gone on for far longer than a reply may take");
        answer.Error.Code.Should().Be(ChatErrorCodes.TimedOut, "the provider answered; it is the model that stalled");
        upstream.Requests.Should().ContainSingle("asking the same stalled model again would only wait again");
    }

    private static ServiceProvider BuildProvider(RoutingHandler upstream, int requestsPerMinute, int? responseTimeoutSeconds = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["AI:OpenRouter:PerMinuteRequestBudget"] = requestsPerMinute.ToString(CultureInfo.InvariantCulture)
        };
        if (responseTimeoutSeconds is { } seconds)
        {
            settings["AI:ResponseTimeoutSeconds"] = seconds.ToString(CultureInfo.InvariantCulture);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiServicesIfConfigured(configuration);

        foreach (var name in new[] { RefitChatModelClient.HttpClientName, RefitChatModelClient.HealthHttpClientName })
        {
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => upstream);
        }

        return services.BuildServiceProvider();
    }

    private const string ChatBody = """
        {"model": "a/model:free", "choices": [{"message": {"role": "assistant", "content": "An answer."}}]}
        """;

    /// <summary>
    /// Padding, and then nothing: the body of the night a model stalled. It does end — with an answer,
    /// after far longer than the test allows a request — so that a guard gone missing fails the test
    /// instead of hanging the run.
    /// </summary>
    private sealed class StalledBody : HttpContent
    {
        private static readonly TimeSpan FarTooLong = TimeSpan.FromSeconds(20);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync("\n\n\n"u8.ToArray(), cancellationToken);
            await Task.Delay(FarTooLong, cancellationToken);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(ChatBody), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>Answers each request with the status its path is given, recording every path that reached it.</summary>
    private sealed class RoutingHandler(Func<string, HttpStatusCode> statusFor, Func<HttpContent>? body = null) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (_requests)
            {
                _requests.Add(path);
            }

            return Task.FromResult(new HttpResponseMessage(statusFor(path))
            {
                Content = body?.Invoke() ?? new StringContent(ChatBody),
                RequestMessage = request
            });
        }
    }
}
