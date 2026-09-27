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
    [InlineData(ChatErrorCodes.RequestFailed, true)]
    [InlineData(ChatErrorCodes.EmptyResponse, true)]
    [InlineData(ChatErrorCodes.RateLimited, false)]
    [InlineData(ChatErrorCodes.Unreachable, false)]
    [InlineData(ChatErrorCodes.Rejected, false)]
    [InlineData(ChatErrorCodes.NoModelAvailable, false)]
    public void BlamesModels_OnlyForFailuresThatSaySomethingAboutThem(string code, bool blames) =>
        // An outage blamed on the chain would demote the three best models on the roster at once.
        AnswerAttemptPolicy.BlamesModels(Error.Unexpected(code, "x")).Should().Be(blames);
}
