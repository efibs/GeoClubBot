using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.KnowledgeGaps;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The report: weak retrievals and 👎-rated questions together, grouped by country and topic, the largest group first.</summary>
public sealed class ReadKnowledgeGapReportHandlerTests
{
    private readonly IAiRetrievalGapRepository _gaps = Substitute.For<IAiRetrievalGapRepository>();
    private readonly IAiFeedbackRepository _feedbacks = Substitute.For<IAiFeedbackRepository>();
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AiKnowledgeGapsConfiguration _settings = new() { ExamplesPerGroup = 2 };

    public ReadKnowledgeGapReportHandlerTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "brazil"]));
        _feedbacks.ReadForExportAsync(AiFeedbackRating.Negative, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiAnswerFeedback>>([Rated("Which bollards does Vietnam use?", "wrong country")]));
    }

    [Fact]
    public async Task GroupsByCountryAndTopic_CountingEachReason_LargestFirst()
    {
        _gaps.ReadSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AiRetrievalGap>>(
        [
            Gap("vietnam", "bollards", AiRetrievalGapReason.NothingFromTheCountry, "Vietnam bollards?", 1),
            Gap("vietnam", "bollards", AiRetrievalGapReason.NothingCited, "Bollards in Vietnam's north?", 2),
            Gap("brazil", null, AiRetrievalGapReason.NoExcerpts, "Brazil gen 4?", 3)
        ]));

        var result = await Handler().Handle(new ReadKnowledgeGapReportQuery(Days: 7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value;
        report.Days.Should().Be(7);
        report.WeakRetrievals.Should().Be(3);
        report.Downrated.Should().Be(1);

        report.Groups[0].Country.Should().Be("vietnam");
        report.Groups[0].NothingFromTheCountry.Should().Be(1);
        report.Groups[0].NothingCited.Should().Be(1);
        report.Groups[0].Examples.Should().HaveCount(2);

        report.Groups.Should().Contain(group => group.Country == "brazil" && group.NoExcerpts == 1);
        report.Groups.Sum(group => group.Downrated).Should().Be(1);
        report.Groups.SelectMany(group => group.Examples).Should().Contain(example => example.Contains("👎 \"wrong country\""));

        await _gaps.Received(1).ReadSinceAsync(_clock.GetUtcNow() - TimeSpan.FromDays(7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsNothing_WhenNothingWentWrong()
    {
        _gaps.ReadSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<AiRetrievalGap>>([]));
        _feedbacks.ReadForExportAsync(Arg.Any<AiFeedbackRating?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AiAnswerFeedback>>([]));

        var report = (await Handler().Handle(new ReadKnowledgeGapReportQuery(), CancellationToken.None)).Value;

        report.Days.Should().Be(30);
        report.Groups.Should().BeEmpty();
        KnowledgeGapReportText.Render(report).Should().Contain("Nothing to report");
    }

    [Fact]
    public void Render_ListsEachGroupWithItsReasonsAndExamples()
    {
        var report = new KnowledgeGapReport(30, 3, 1,
        [
            new KnowledgeGapGroup("vietnam", "bollards", 0, 1, 1, 1, ["Vietnam bollards?"]),
            new KnowledgeGapGroup(null, null, 1, 0, 0, 0, [])
        ]);

        var text = KnowledgeGapReportText.Render(report);

        text.Should().StartWith("📉 **Knowledge gaps — last 30 days**\n3 question(s) with a weak retrieval, 1 answer(s) rated 👎.");
        text.Should().Contain("**Vietnam** · bollards — 1 nothing from the country, 1 nothing cited, 1 rated 👎\n-# “Vietnam bollards?”");
        text.Should().Contain("**No country named** — 1 nothing found");
    }

    private ReadKnowledgeGapReportHandler Handler()
    {
        var directory = new KnowledgeCountryDirectory(_index, Options.Create(new AiRetrievalConfiguration()), _clock, NullLogger<KnowledgeCountryDirectory>.Instance);
        return new ReadKnowledgeGapReportHandler(_gaps, _feedbacks, directory, Options.Create(_settings), _clock);
    }

    private AiRetrievalGap Gap(string? country, string? topic, AiRetrievalGapReason reason, string question, int hoursAgo) =>
        AiRetrievalGap.Create(_clock.GetUtcNow() - TimeSpan.FromHours(hoursAgo), question, country, topic, reason);

    private AiAnswerFeedback Rated(string question, string comment)
    {
        var asked = AiConversationTurn.CreateUserTurn(1, null, 1, 1, 1, 7, question, null, 0, _clock.GetUtcNow());
        var answered = AiConversationTurn.CreateAssistantTurn(2, 1, 1, 1, 1, 99, "An answer.", "test/model", [], [], [], 1, _clock.GetUtcNow());
        return AiAnswerFeedback.Create([asked, answered], 9, AiFeedbackRating.Negative, comment, _clock.GetUtcNow());
    }
}
