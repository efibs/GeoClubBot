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
/// breaker by itself, and every question then failed for two minutes although chat was answering.
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

    private static ServiceProvider BuildProvider(RoutingHandler upstream, int requestsPerMinute)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:OpenRouter:PerMinuteRequestBudget"] = requestsPerMinute.ToString(CultureInfo.InvariantCulture)
            })
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

    /// <summary>Answers each request with the status its path is given, recording every path that reached it.</summary>
    private sealed class RoutingHandler(Func<string, HttpStatusCode> statusFor) : HttpMessageHandler
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
                Content = new StringContent(ChatBody),
                RequestMessage = request
            });
        }
    }
}
