using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

public sealed class AnswerAttemptPolicyTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(ChatErrorCodes.RequestFailed)]
    [InlineData(ChatErrorCodes.EmptyResponse)]
    [InlineData(ChatErrorCodes.UnusableAnswer)]
    [InlineData(ChatErrorCodes.ToolCallMarkup)]
    public void ShouldRetry_AfterAQuickFailureOtherModelsCouldFix(string code) =>
        AnswerAttemptPolicy.ShouldRetry(Error.Unexpected(code, "x"), TimeSpan.FromSeconds(3), Patience)
            .Should().BeTrue();

    [Theory]
    [InlineData(ChatErrorCodes.RateLimited)]
    [InlineData(ChatErrorCodes.Unreachable)]
    [InlineData(ChatErrorCodes.Rejected)]
    [InlineData(ChatErrorCodes.NoModelAvailable)]
    public void ShouldRetry_NotWhenOtherModelsSitBehindTheSameProblem(string code) =>
        // Every model sits behind the same provider and the same key: a retry only adds to the wait.
        AnswerAttemptPolicy.ShouldRetry(Error.Unexpected(code, "x"), TimeSpan.FromSeconds(3), Patience)
            .Should().BeFalse();

    [Fact]
    public void ShouldRetry_NotAfterASlowAttempt() =>
        // A provider that took this long to fail is struggling; asking again doubles the wait.
        AnswerAttemptPolicy.ShouldRetry(
                Error.Unexpected(ChatErrorCodes.RequestFailed, "x"), TimeSpan.FromSeconds(61), Patience)
            .Should().BeFalse();

    [Theory]
    [InlineData(ChatErrorCodes.ModelGated)]
    [InlineData(ChatErrorCodes.ModelRateLimited)]
    public void ShouldRetry_WhenOnlyTheFirstModelWasTheProblem(string code) =>
        // Measured on 2026-10-23: inkling answered 403 "only available on agentic harnesses" and gemma a
        // shared-pool 429, while Nemotron Ultra and Super VL answered the same request at once. Refusing
        // the retry, as a plain 403 or 429 is refused, would fail a question other models could answer.
        AnswerAttemptPolicy.ShouldRetry(Error.Unexpected(code, "x"), TimeSpan.FromSeconds(1), Patience)
            .Should().BeTrue();

    [Theory]
    [InlineData(ChatErrorCodes.RequestFailed, AnswerFailureBlame.WholeChain)]
    [InlineData(ChatErrorCodes.EmptyResponse, AnswerFailureBlame.WholeChain)]
    [InlineData(ChatErrorCodes.ModelRateLimited, AnswerFailureBlame.FirstModel)]
    [InlineData(ChatErrorCodes.ModelGated, AnswerFailureBlame.FirstModelUnfit)]
    [InlineData(ChatErrorCodes.RateLimited, AnswerFailureBlame.None)]
    [InlineData(ChatErrorCodes.Unreachable, AnswerFailureBlame.None)]
    [InlineData(ChatErrorCodes.Rejected, AnswerFailureBlame.None)]
    [InlineData(ChatErrorCodes.NoModelAvailable, AnswerFailureBlame.None)]
    [InlineData(ChatErrorCodes.UnusableAnswer, AnswerFailureBlame.None)]
    [InlineData(ChatErrorCodes.ToolsUnsupported, AnswerFailureBlame.None)]
    // A good model's formatting slip under tool_choice "none" (nemotron-3-ultra, 2026-10-29).
    [InlineData(ChatErrorCodes.ToolCallMarkup, AnswerFailureBlame.None)]
    public void AssignBlame_OnlyForFailuresThatSaySomethingAboutTheModels(string code, AnswerFailureBlame blame) =>
        // An outage blamed on the chain would demote the three best models on the roster at once, and a
        // busy or gated head blamed on the whole chain would demote the healthy models behind it.
        AnswerAttemptPolicy.AssignBlame(Error.Unexpected(code, "x")).Should().Be(blame);
}
