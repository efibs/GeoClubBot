using FluentAssertions;
using UseCases.UseCases.AI.Progress;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The steps an answer reports, and the ambient log a step deep in a singleton reports to.</summary>
public sealed class AiProgressLogTests
{
    [Fact]
    public void Step_IsDone_OnceItsHandleIsDisposed()
    {
        var log = new AiProgressLog();

        var step = log.Begin(AiProgressKind.LookingUpPlant, "  Pinus kesiya ");
        log.Snapshot().Should().Equal(new AiProgressStep(AiProgressKind.LookingUpPlant, "Pinus kesiya", Done: false));

        step.Dispose();
        step.Dispose();
        log.Snapshot().Should().Equal(new AiProgressStep(AiProgressKind.LookingUpPlant, "Pinus kesiya", Done: true));
    }

    [Fact]
    public void Changed_IsRaised_WhenAStepBeginsAndWhenItIsDone_Once()
    {
        var log = new AiProgressLog();
        var changes = 0;
        log.Changed += () => changes++;

        var step = log.Begin(AiProgressKind.Thinking);
        step.Dispose();
        step.Dispose();

        changes.Should().Be(2);
    }

    [Fact]
    public void Begin_KeepsNoMoreThanTheCap()
    {
        var log = new AiProgressLog();

        for (var index = 0; index < AiProgressLog.MaxSteps + 5; index++)
        {
            log.Begin(AiProgressKind.SearchingGuidesFor, $"query {index}").Dispose();
        }

        log.Snapshot().Should().HaveCount(AiProgressLog.MaxSteps);
    }

    [Fact]
    public async Task Step_ReportsToTheTrackedLog_AcrossAwaitsAndStartedTasks()
    {
        var log = new AiProgressLog();

        using (AiProgress.Track(log))
        {
            await Task.Yield();
            await Task.Run(() => AiProgress.Step(AiProgressKind.IdentifyingPlant).Dispose());
            using (AiProgress.Step(AiProgressKind.Thinking))
            {
                await Task.Delay(1);
            }
        }

        AiProgress.Current.Should().BeNull("the log is only tracked until its handle is disposed");
        log.Snapshot().Select(step => (step.Kind, step.Done)).Should().Equal(
            (AiProgressKind.IdentifyingPlant, true),
            (AiProgressKind.Thinking, true));
    }

    [Fact]
    public void Step_DoesNothing_WithoutATrackedLog()
    {
        AiProgress.Current.Should().BeNull();

        var act = () => AiProgress.Step(AiProgressKind.Thinking).Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ConcurrentAnswers_ReportToTheirOwnLogs()
    {
        var first = new AiProgressLog();
        var second = new AiProgressLog();

        await Task.WhenAll(AnswerAsync(first, "first"), AnswerAsync(second, "second"));

        first.Snapshot().Should().ContainSingle().Which.Detail.Should().Be("first");
        second.Snapshot().Should().ContainSingle().Which.Detail.Should().Be("second");

        static async Task AnswerAsync(AiProgressLog log, string detail)
        {
            using var tracking = AiProgress.Track(log);
            await Task.Yield();
            using var step = AiProgress.Step(AiProgressKind.SearchingGuidesFor, detail);
            await Task.Delay(5);
        }
    }
}
