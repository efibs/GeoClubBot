using Entities;
using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.KnowledgeGaps;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>What counts as a weak retrieval: nothing offered, nothing from the country asked about, or nothing cited.</summary>
public sealed class WeakRetrievalTests
{
    private static readonly CountryGazetteer Gazetteer = CountryGazetteer.Build(["vietnam", "usa", "united states", "brazil"]);

    [Fact]
    public void Subject_ReadsTheOneCountry_UnderEverySpelling_AndTheTopic()
    {
        var subject = WeakRetrieval.Subject("What do the utility poles look like in the US?", Gazetteer);

        subject.Country.Should().Be("united states");
        subject.IndexNames.Should().BeEquivalentTo(["usa", "united states"]);
        subject.Topic.Should().NotBeNull();
    }

    [Fact]
    public void Subject_NamesNoCountry_WhenTheQuestionNamesSeveral() =>
        WeakRetrieval.Subject("Vietnam or Brazil?", Gazetteer).Country.Should().BeNull();

    [Fact]
    public void Judge_NothingOffered_IsNoExcerpts() =>
        WeakRetrieval.Judge(Subject("vietnam"), [], cited: 0).Should().Be(AiRetrievalGapReason.NoExcerpts);

    [Fact]
    public void Judge_NothingFromTheCountryAsked_IsAGapEvenWhenCited() =>
        WeakRetrieval.Judge(Subject("vietnam"), [Hit("brazil")], cited: 1).Should().Be(AiRetrievalGapReason.NothingFromTheCountry);

    [Fact]
    public void Judge_AnExcerptUnderAnotherSpellingOfTheCountry_Counts() =>
        WeakRetrieval.Judge(new QuestionSubject("united states", ["usa", "united states"], null), [Hit("USA")], cited: 1).Should().BeNull();

    [Fact]
    public void Judge_NothingCited_IsAGap() =>
        WeakRetrieval.Judge(Subject("vietnam"), [Hit("vietnam")], cited: 0).Should().Be(AiRetrievalGapReason.NothingCited);

    [Fact]
    public void Judge_AQuestionWithoutACountry_IsJudgedByItsCitations() =>
        WeakRetrieval.Judge(new QuestionSubject(null, [], null), [Hit("brazil")], cited: 2).Should().BeNull();

    private static QuestionSubject Subject(string country) => new(country, [country], null);

    private static KnowledgeHit Hit(string country) =>
        new(Guid.NewGuid(), 0.5f, KnowledgeChunkKind.Text, "text", "https://guide", null, "Guide", country, "Guide > Poles", null, 0);
}
