using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// What other guides say that PlonkIt says too. The model is shown excerpts, not guides, and cannot
/// know; the index can, from the vectors it holds.
/// </summary>
public sealed class MainGuideOverlapTests
{
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly AiRetrievalConfiguration _settings = new();

    public MainGuideOverlapTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["india", "usa", "united states", "ukraine"]));
    }

    [Fact]
    public async Task MarksTheExcerpts_PlonkItHasATipLike()
    {
        // The Sikkim poles scored 0.61 against PlonkIt's "Metal poles with a mesh pattern are commonly
        // found in Sikkim"; the Dasuya forest car has nothing like it there.
        var poles = Hit("gdoc", "india", "These metal telephone poles are pretty much exclusively found in Sikkim.");
        var forestCar = Hit("gdoc", "india", "The Dasuya forest has been covered with a very unusual car.");
        Nearest((poles, 0.611f), (forestCar, 0.48f));

        var marked = await Overlap().MarkAsync([poles, forestCar], CancellationToken.None);

        marked.Select(hit => hit.AlsoInMainGuide).Should().Equal(true, false);
        marked.Select(hit => hit.Id).Should().Equal(poles.Id, forestCar.Id);
    }

    [Fact]
    public async Task ComparesWithPlonkItsTips_UnderEverySpellingOfTheCountry()
    {
        // A document's "usa" is PlonkIt's "united states": compared under its own spelling, "28 Secret
        // US Metas" found no PlonkIt tip of its country at all.
        var bollard = Hit("gdoc", "usa", "This bollard is extremely common in Oregon.");
        Nearest((bollard, 0.687f));

        await Overlap().MarkAsync([bollard], CancellationToken.None);

        await _index.Received(1).ReadNearestOfSourceAsync(
            Arg.Is<IReadOnlyList<KnowledgeHit>>(hits => hits.Count == 1 && hits[0].Id == bollard.Id),
            "plonkit",
            Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(spellings =>
                spellings["usa"].Contains("usa") && spellings["usa"].Contains("united states")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ComparesNeitherPlonkItsOwnTips_NorWhatIsNoChunkOfTheIndex()
    {
        var tip = Hit("plonkit", "india", "Metal poles with a mesh pattern are commonly found in Sikkim.");
        var speciesRange = Hit(null, null, "Pinus kesiya (species).");

        var marked = await Overlap().MarkAsync([tip, speciesRange], CancellationToken.None);

        marked.Should().OnlyContain(hit => !hit.AlsoInMainGuide);
        await _index.DidNotReceiveWithAnyArgs().ReadNearestOfSourceAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task MarksNothing_WhenTurnedOff_OrWhenTheComparisonFails()
    {
        var poles = Hit("gdoc", "india", "Metal poles.");
        Nearest((poles, 0.9f));

        _settings.LesserKnown.Enabled = false;
        (await Overlap().MarkAsync([poles], CancellationToken.None)).Should().OnlyContain(hit => !hit.AlsoInMainGuide);

        _settings.LesserKnown.Enabled = true;
        _index.ReadNearestOfSourceAsync(default!, default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("Index down."));
        (await Overlap().MarkAsync([poles], CancellationToken.None)).Should().OnlyContain(hit => !hit.AlsoInMainGuide);
    }

    [Fact]
    public async Task TheBarIsASetting()
    {
        var poles = Hit("gdoc", "india", "Metal poles.");
        Nearest((poles, 0.6f));
        _settings.LesserKnown.SameClueSimilarity = 0.7;

        (await Overlap().MarkAsync([poles], CancellationToken.None)).Should().OnlyContain(hit => !hit.AlsoInMainGuide);
    }

    private MainGuideOverlap Overlap()
    {
        var retrieval = Options.Create(_settings);
        return new MainGuideOverlap(
            _index,
            new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance),
            retrieval,
            NullLogger<MainGuideOverlap>.Instance);
    }

    private void Nearest(params (KnowledgeHit Hit, float Score)[] pairs) =>
        _index.ReadNearestOfSourceAsync(default!, default!, default, default).ReturnsForAnyArgs(
            Task.FromResult<IReadOnlyDictionary<Guid, KnowledgeHit>>(pairs.ToDictionary(
                pair => pair.Hit.Id,
                pair => Hit("plonkit", pair.Hit.Country, "A PlonkIt tip.") with { Score = pair.Score })));

    private static KnowledgeHit Hit(string? sourceType, string? country, string text) =>
        new(Guid.NewGuid(), 0.9f, KnowledgeChunkKind.Text, text, "https://guide.example/a", null, "Guide", country, "Guide > Section",
            null, 0, sourceType, sourceType is null ? null : "key", "p1");
}
