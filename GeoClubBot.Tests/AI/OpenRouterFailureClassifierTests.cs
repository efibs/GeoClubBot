using FluentAssertions;
using Infrastructure.OutputAdapters.AI.OpenRouter;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The bodies are the ones OpenRouter returned on 2026-10-23 (the gemma 429 trimmed of its remedy text),
/// so the classifier is pinned to the wire, not to a guess at it.
/// </summary>
public sealed class OpenRouterFailureClassifierTests
{
    internal const string GatedInkling = """
        {"error": {"message": "thinkingmachines/inkling:free is only available on agentic harnesses. Try plugging it into a coding agent or productivity app listed on https://openrouter.ai/apps", "code": 403, "metadata": {"routing_funnel": [{"step": "Initial Endpoints", "endpoint_count": 1}], "failed_routing_step": "Gate Free Endpoints by Agentic Harness"}}}
        """;

    internal const string BusyGemma = """
        {"error": {"message": "Provider returned error", "code": 429, "metadata": {"raw": "google/gemma-4-31b-it:free is temporarily rate-limited upstream. Please retry shortly, or add your own key to accumulate your rate limits: https://openrouter.ai/settings/integrations", "provider_name": "Google AI Studio", "is_byok": false, "provider_error_code": "429", "limit_source": "upstream_provider_shared_pool"}}, "user_id": "user_x"}
        """;

    [Fact]
    public void Classify_RecognisesAModelReservedForAgenticHarnesses_AndTheModelItNames()
    {
        var failure = OpenRouterFailureClassifier.Classify(GatedInkling);

        failure.Kind.Should().Be(OpenRouterFailureKind.AgenticHarnessGate);
        failure.NamedModelId.Should().Be("thinkingmachines/inkling:free");
        failure.NamesHead(["thinkingmachines/inkling:free", "b/model"]).Should().BeTrue();
        failure.NamesHead(["b/model", "thinkingmachines/inkling:free"]).Should().BeFalse();
    }

    [Fact]
    public void Classify_RecognisesTheGate_ByItsRoutingStep_ShouldTheWordingChange()
    {
        var failure = OpenRouterFailureClassifier.Classify("""
            {"error": {"message": "Forbidden", "code": 403, "metadata": {"failed_routing_step": "Gate Free Endpoints by Agentic Harness"}}}
            """);

        failure.Kind.Should().Be(OpenRouterFailureKind.AgenticHarnessGate);
        failure.NamedModelId.Should().BeNull();
    }

    [Fact]
    public void Classify_RecognisesAProvidersSharedPoolRunningDry_AndTheModelItNames()
    {
        // The outer message is a generic "Provider returned error"; the provider's own text names the model.
        var failure = OpenRouterFailureClassifier.Classify(BusyGemma);

        failure.Kind.Should().Be(OpenRouterFailureKind.UpstreamRateLimit);
        failure.NamedModelId.Should().Be("google/gemma-4-31b-it:free");
        OpenRouterFailureClassifier.IsUpstreamRateLimit(BusyGemma).Should().BeTrue();
    }

    [Fact]
    public void Classify_RecognisesAnUpstreamLimit_FromItsTextAlone()
    {
        var failure = OpenRouterFailureClassifier.Classify("""
            {"error": {"message": "Provider returned error", "code": 429, "metadata": {"raw": "a/model is temporarily rate-limited upstream."}}}
            """);

        failure.Kind.Should().Be(OpenRouterFailureKind.UpstreamRateLimit);
        failure.NamedModelId.Should().Be("a/model");
    }

    [Theory]
    // Our own key's window: the 429 every free key meets past 20 requests a minute.
    [InlineData("""{"error": {"message": "Rate limit exceeded: free-models-per-min.", "code": 429, "metadata": {"headers": {"X-RateLimit-Limit": "20"}}}}""")]
    [InlineData("""{"error": {"message": "Key limit exceeded", "code": 403}}""")]
    [InlineData("""{"error": "not an object"}""")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_SaysNothing_AboutAnythingElse(string? body) =>
        OpenRouterFailureClassifier.Classify(body).Should().Be(OpenRouterFailure.Other);
}
