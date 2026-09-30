using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The first two positives are verbatim what beta testers were shown as answers. The negatives matter
/// as much: a detector that fired on a real answer would throw it away and spend a retry replacing it.
/// </summary>
public sealed class GuardrailVerdictDetectorTests
{
    [Theory]
    [InlineData("User Safety: safe")]
    [InlineData("User Safety: safe\nResponse Safety: safe")]
    [InlineData("User Safety: unsafe\nSafety Categories: Violence, Criminal Planning/Confessions")]
    [InlineData("**User Safety:** safe")]
    [InlineData("{\"User Safety\": \"safe\", \"Response Safety\": \"safe\"}")]
    [InlineData("safe")]
    [InlineData("unsafe\nS1,S10")]
    [InlineData("Safety: Safe\nCategories: None")]
    [InlineData("Safety: Controversial\nCategories: Politically Sensitive Topics\nRefusal: No")]
    [InlineData("Harmful request: no\nResponse refusal: no\nHarmful response: no")]
    public void IsVerdict_RecognisesWhatSafetyClassifiersAnswerWith(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeTrue();

    [Theory]
    [InlineData("Road safety barriers in Chile are painted yellow and black.")]
    [InlineData("User Safety: safe\n\nThe sign is written in the Bengali-Assamese script.")]
    [InlineData("Safe.")]
    [InlineData("No.")]
    [InlineData("Yes")]
    [InlineData("Category: Bollards\nCountry: Ghana")]
    [InlineData("{\"answer\": \"Ghana\"}")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsVerdict_LeavesRealAnswersAlone(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeFalse();

    [Fact]
    public void IsVerdict_IgnoresALongAnswerThatQuotesAVerdictLine()
    {
        var reply = "Response Safety: safe\n" + string.Concat(Enumerable.Repeat("Bollards here are white. ", 30));

        GuardrailVerdictDetector.IsVerdict(reply).Should().BeFalse();
    }

    [Fact]
    public void IsVerdict_IsFalseForNull() =>
        GuardrailVerdictDetector.IsVerdict(null).Should().BeFalse();
}
