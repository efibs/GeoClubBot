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

    [Theory]
    [InlineData("Response Safety: safe")]
    [InlineData("Prompt Safety: safe")]
    [InlineData("Safety: safe")]
    [InlineData("Harmful request: yes")]
    [InlineData("Harmful response: no")]
    [InlineData("Safety: harmful")]
    [InlineData("Safety: unharmful")]
    [InlineData("Safety: unsafe")]
    [InlineData("Safety: controversial")]
    [InlineData("Safety: safe\nSafety Categories: None")]
    [InlineData("Safety: safe\nSafety Category: None")]
    [InlineData("Safety: safe\nCategories: None")]
    [InlineData("Safety: safe\nCategory: None")]
    [InlineData("Safety: safe\nRefusal: no")]
    [InlineData("Safety: safe\nResponse refusal: no")]
    [InlineData("User_Safety: **safe.**")]
    [InlineData("Safety: safe\n\n   Categories: None   ")]
    public void IsVerdict_KnowsEveryLabelAndValue(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeTrue();

    [Theory]
    [InlineData("{\"Safety\": \"safe\", \"Categories\": \"None\"}")]
    [InlineData("{\"Harmful response\": \"no\"}")]
    public void IsVerdict_ReadsAJsonVerdict(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeTrue();

    [Theory]
    [InlineData("{\"Safety\": \"safe\", \"answer\": \"Ghana\"}")]
    [InlineData("{\"Safety\": 1}")]
    [InlineData("{\"Safety\": \"maybe\"}")]
    [InlineData("{\"Categories\": \"safe\"}")]
    [InlineData("{not json")]
    [InlineData("{}")]
    public void IsVerdict_TakesJsonOnlyWhenAllOfItIsAVerdict(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeFalse();

    [Theory]
    [InlineData("Safety: safe\nCountry: Ghana")]
    [InlineData("Safety: maybe")]
    [InlineData("Categories: safe")]
    [InlineData("Categories: None")]
    public void IsVerdict_NeedsAVerdictLine_AndNothingButKnownLabels(string reply) =>
        GuardrailVerdictDetector.IsVerdict(reply).Should().BeFalse();

    [Fact]
    public void IsVerdict_ReadsAVerdictUpToItsMaximumLength()
    {
        const string start = "Safety: safe\nCategories: ";

        GuardrailVerdictDetector.IsVerdict(start + new string('x', 400 - start.Length)).Should().BeTrue();
        GuardrailVerdictDetector.IsVerdict(start + new string('x', 401 - start.Length)).Should().BeFalse();
    }
}
