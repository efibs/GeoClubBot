using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.UseCases.AI.Progress;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

/// <summary>The small status reply a member sees while an answer is made (#481).</summary>
public sealed class AiProgressFormatterTests
{
    [Fact]
    public Task Render_TicksWhatIsDone_AndShowsWhatIsRunning() =>
        Verify(AiProgressFormatter.Render(
        [
            new AiProgressStep(AiProgressKind.EmbeddingScreenshot, null, Done: true),
            new AiProgressStep(AiProgressKind.IdentifyingPlant, null, Done: true),
            new AiProgressStep(AiProgressKind.SearchingGuides, null, Done: true),
            new AiProgressStep(AiProgressKind.LookingUpPlant, "Pinus kesiya", Done: true),
            new AiProgressStep(AiProgressKind.DrawingRangeMap, "Pinus kesiya", Done: false),
            new AiProgressStep(AiProgressKind.Thinking, null, Done: false)
        ]));

    [Fact]
    public void Render_ShowsNothing_BeforeTheFirstStep() =>
        AiProgressFormatter.Render([]).Should().BeNull();

    [Fact]
    public void Render_FoldsOlderSteps_IntoOneLine()
    {
        var steps = Enumerable.Range(1, AiProgressFormatter.MaxLines + 3)
            .Select(index => new AiProgressStep(AiProgressKind.SearchingGuidesFor, $"query {index}", Done: true))
            .ToList();

        var lines = AiProgressFormatter.Render(steps)!.Split('\n');

        lines.Should().HaveCount(AiProgressFormatter.MaxLines);
        lines[0].Should().Be("-# … 4 earlier steps");
        lines[^1].Should().Be($"-# ✓ Searching the guides for “query {AiProgressFormatter.MaxLines + 3}”");
    }

    [Fact]
    public void Describe_KeepsTheModelsWordsOnOneShortLine_WithoutFormatting()
    {
        var detail = "**bold** _pine_\nwith `code` " + new string('x', 200);

        var text = AiProgressFormatter.Describe(new AiProgressStep(AiProgressKind.SearchingGuidesFor, detail, Done: false));

        text.Should().StartWith(@"Searching the guides for “\*\*bold\*\* \_pine\_ with \`code\` x");
        text.Should().NotContain("\n");
        text.Should().EndWith("…”");
    }

    [Fact]
    public void Describe_NamesEveryKind()
    {
        foreach (var kind in Enum.GetValues<AiProgressKind>())
        {
            AiProgressFormatter.Describe(new AiProgressStep(kind, "x", Done: false))
                .Should().NotBe("Working", $"{kind} needs words of its own");
        }
    }
}
