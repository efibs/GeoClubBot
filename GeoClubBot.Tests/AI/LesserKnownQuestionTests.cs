using Entities;
using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Which questions ask for something that is not common knowledge. To a player that means "not on
/// PlonkIt": three answers in a row were rated down for giving clues it carries (2026-11-09).
/// </summary>
public sealed class LesserKnownQuestionTests
{
    [Theory]
    [InlineData("tell me a reliable clue I don't know yet.")]
    [InlineData("please tell me a good USA regionguessing clue that is not on PlonkIt.")]
    [InlineData("Any metas for Brazil that aren't on Plonk It?")]
    [InlineData("what's a clue beyond plonkit for Kenya")]
    [InlineData("Give me a lesser-known clue for Japan")]
    [InlineData("an obscure Mongolia meta please")]
    [InlineData("is there a niche trick for telling Czechia from Slovakia?")]
    [InlineData("Tell me something new about Indonesian poles")]
    [InlineData("teach me a meta few players know")]
    [InlineData("a clue that is not well known")]
    [InlineData("any secret US metas?")]
    public void AsksForSomethingNew(string question) =>
        LesserKnownQuestion.Asks(question).Should().BeTrue();

    [Theory]
    [InlineData("where can I find Khasi pines?")]
    [InlineData("what does PlonkIt say about bollards in Kenya?")]
    [InlineData("Is this on PlonkIt?")]
    [InlineData("I don't know where this is, can you help?")]
    [InlineData("what are the known car metas of Russia?")]
    [InlineData("how do I tell the new and the old plates apart?")]
    [InlineData("")]
    [InlineData(null)]
    public void AsksNothingOfTheKind(string? question) =>
        LesserKnownQuestion.Asks(question).Should().BeFalse();

    [Fact]
    public void AFollowUpStillAsksIt_WhenTheQuestionsBeforeItDid()
    {
        // "The sikkim pole is actually also on PlonkIt … Please give me another one, maybe outside India."
        // was answered with two more clues PlonkIt carries.
        var branch = new[]
        {
            new ConversationTurnView(AiTurnRole.User, 1, "tell me a reliable clue I don't know yet.", []),
            new ConversationTurnView(AiTurnRole.Assistant, 2, "India (Sikkim): metal telephone poles.", [])
        };

        LesserKnownQuestion.Asks("Please give me another one, maybe outside India.", branch).Should().BeTrue();
        LesserKnownQuestion.Asks("Please give me another one, maybe outside India.", []).Should().BeFalse();
    }

    [Theory]
    [InlineData("tell me a reliable clue I don't know yet.")]
    [InlineData("Give me a good meta that's not on PlonkIt, please")]
    [InlineData("teach me something new")]
    [InlineData("Surprise me with a lesser-known tip!")]
    [InlineData("any niche metas most players don’t know?")]
    [InlineData("another one please")]
    public void NamesNoSubject_WhenItIsMadeOfNothingButTheRequest(string question) =>
        LesserKnownQuestion.NamesNoSubject(question).Should().BeTrue();

    [Theory]
    [InlineData("please tell me a good USA regionguessing clue that is not on PlonkIt.")]
    [InlineData("Give me a lesser-known clue for Japan")]
    [InlineData("a lesser-known bollard clue")]
    [InlineData("any secret Gen 4 metas?")]
    [InlineData("The sikkim pole is actually also on PlonkIt, so I knew that already. Please give me another one, maybe outside India.")]
    [InlineData("?!")]
    [InlineData("")]
    [InlineData(null)]
    public void NamesASubject_WhenAnyOtherWordStandsInIt(string? question) =>
        LesserKnownQuestion.NamesNoSubject(question).Should().BeFalse();

    [Fact]
    public void AFollowUpNamesNoSubject_OnlyWhenTheQuestionsBeforeItNamedNone()
    {
        ConversationTurnView[] open =
        [
            new(AiTurnRole.User, 1, "tell me a reliable clue I don't know yet.", []),
            new(AiTurnRole.Assistant, 2, "Indonesia (Riau): poles with a huge squared X.", [])
        ];
        ConversationTurnView[] aboutBollards =
        [
            new(AiTurnRole.User, 1, "any lesser-known bollard clues?", []),
            new(AiTurnRole.Assistant, 2, "Slovenia's bollards have a red strip.", [])
        ];

        LesserKnownQuestion.NamesNoSubject("another one please", open).Should().BeTrue();
        LesserKnownQuestion.NamesNoSubject("another one please", aboutBollards)
            .Should().BeFalse("\"another one\" is another bollard clue");
    }

    [Fact]
    public void OnlyTheNearestQuestionsOfABranchCount()
    {
        var branch = new List<ConversationTurnView>
        {
            new(AiTurnRole.User, 1, "tell me an obscure clue", [])
        };
        branch.AddRange(Enumerable.Range(0, 3).SelectMany(_ => new[]
        {
            new ConversationTurnView(AiTurnRole.Assistant, 2, "An answer.", []),
            new ConversationTurnView(AiTurnRole.User, 1, "and what about the bollards there?", [])
        }));

        LesserKnownQuestion.Asks("which poles do they use?", branch).Should().BeFalse("the branch has long moved on");
    }
}
