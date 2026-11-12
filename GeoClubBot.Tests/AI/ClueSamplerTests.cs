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
/// The picture clues drawn at random for "tell me a reliable clue I don't know yet": a question a
/// search can only match by its wording, which found a document's legend and no picture (2026-11-11).
/// </summary>
public sealed class ClueSamplerTests
{
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly AiRetrievalConfiguration _settings = new();

    public ClueSamplerTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["india", "indonesia", "peru"]));
    }

    [Fact]
    public async Task DrawsPictureClues_FromEveryGuideButPlonkIt()
    {
        var riau = Clue("indonesia-poles", "Riau: very tall poles with a huge squared X, entirely unique to the province.");
        var peru = Clue("peru-poles", "Pole markings painted in yellow and black bands are typical of the Cusco region.");
        Drawn(riau, peru);

        var picks = await Sampler().DrawAsync(CancellationToken.None);

        picks.Select(pick => pick.Id).Should().Equal(riau.Id, peru.Id);
        await _index.Received(1).SampleAsync(
            Arg.Is<KnowledgeSample>(sample =>
                sample.Kind == KnowledgeChunkKind.Image
                && sample.ExcludedSourceType == "plonkit"
                && sample.Limit > _settings.LesserKnown.RandomPicks),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeavesOut_WhatTheModelCouldSayNothingAbout()
    {
        // The model knows a picture only by its text.
        var clue = Clue("a", "Square water tanks with angled corners are found from İskenderun to Şanlıurfa.");
        var bareHeading = Clue("b", "Distribution");
        var itsPath = Clue("c", "A Unique USA > 7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield") with
        {
            SectionPath = "A Unique USA > 7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield"
        };
        var fewWords = Clue("d", "Typical poles of the northern part.");
        var noPicture = Clue("e", "Bollards with a red reflector on a black band are found in the south of the country.") with
        {
            Kind = KnowledgeChunkKind.Text,
            ImageUrl = null
        };
        Drawn(bareHeading, itsPath, fewWords, noPicture, clue);

        var picks = await Sampler().DrawAsync(CancellationToken.None);

        picks.Should().ContainSingle().Which.Id.Should().Be(clue.Id);
    }

    [Fact]
    public async Task TakesOneClueOfAGuide_SoTheChoiceIsBetweenClues()
    {
        var first = Clue("indonesia-poles", "Riau: very tall poles with a huge squared X, entirely unique to the province.");
        var second = Clue("indonesia-poles", "Aceh: short concrete poles with three holes near the top are the common type.");
        var other = Clue("peru-poles", "Pole markings painted in yellow and black bands are typical of the Cusco region.");
        Drawn(first, second, other);

        var picks = await Sampler().DrawAsync(CancellationToken.None);

        picks.Select(pick => pick.Id).Should().Equal(first.Id, other.Id);
    }

    [Fact]
    public async Task LeavesOut_WhatPlonkItTellsToo()
    {
        var sikkim = Clue("india", "These metal telephone poles are pretty much exclusively found in Sikkim.") with { Country = "india" };
        var forestCar = Clue("india-cars", "The Dasuya forest has been covered with a very unusual car, seen nowhere else.") with { Country = "india" };
        Drawn(sikkim, forestCar);
        _index.ReadNearestOfSourceAsync(default!, default!, default, default).ReturnsForAnyArgs(
            Task.FromResult<IReadOnlyDictionary<Guid, KnowledgeHit>>(new Dictionary<Guid, KnowledgeHit>
            {
                [sikkim.Id] = Clue("plonkit-india", "Metal poles with a mesh pattern are commonly found in Sikkim.") with { Score = 0.61f },
                [forestCar.Id] = Clue("plonkit-india", "Canola is mostly found in and around Rajasthan.") with { Score = 0.44f }
            }));

        var picks = await Sampler().DrawAsync(CancellationToken.None);

        picks.Should().ContainSingle().Which.Id.Should().Be(forestCar.Id);
    }

    [Fact]
    public async Task DrawsAsManyAsTheSettingSays_AndNoneAtZero()
    {
        Drawn([.. Enumerable.Range(0, 9).Select(guide =>
            Clue($"guide-{guide}", $"Clue number {guide}: a bollard with a red reflector on a black band, found in the south."))]);

        _settings.LesserKnown.RandomPicks = 2;
        (await Sampler().DrawAsync(CancellationToken.None)).Should().HaveCount(2);

        _index.ClearReceivedCalls();
        _settings.LesserKnown.RandomPicks = 0;
        (await Sampler().DrawAsync(CancellationToken.None)).Should().BeEmpty();
        await _index.DidNotReceiveWithAnyArgs().SampleAsync(default!, default);

        _settings.LesserKnown.RandomPicks = 6;
        _settings.LesserKnown.Enabled = false;
        (await Sampler().DrawAsync(CancellationToken.None)).Should().BeEmpty();
        await _index.DidNotReceiveWithAnyArgs().SampleAsync(default!, default);
    }

    [Fact]
    public async Task DrawsNothing_WhenTheIndexFails_RatherThanFailTheQuestion()
    {
        _index.SampleAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("Index down."));

        (await Sampler().DrawAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public void Lead_PutsTheDrawFirst_AndFillsThePlacesLeftFromTheSearch()
    {
        var drawn = new[] { Clue("a", "Drawn clue A."), Clue("b", "Drawn clue B.") };
        KnowledgeHit[] found = [drawn[1], .. Enumerable.Range(0, 10).Select(number => Clue($"found-{number}", $"Found {number}."))];

        var offered = ClueSampler.Lead(drawn, found, excerptLimit: 10);

        offered.Should().HaveCount(10);
        offered.Take(2).Select(hit => hit.Id).Should().Equal(drawn[0].Id, drawn[1].Id);
        offered.Select(hit => hit.Id).Should().OnlyHaveUniqueItems("what the search found too is offered once");
        offered.Skip(2).Select(hit => hit.Id).Should().Equal(found.Skip(1).Take(8).Select(hit => hit.Id));
    }

    [Fact]
    public void Lead_LeavesTheSearchAsItIs_WithoutADraw()
    {
        KnowledgeHit[] found = [Clue("a", "Found A."), Clue("b", "Found B.")];

        ClueSampler.Lead([], found, excerptLimit: 1).Should().BeSameAs(found);
    }

    private ClueSampler Sampler()
    {
        var retrieval = Options.Create(_settings);
        var countries = new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);
        return new ClueSampler(
            _index,
            new MainGuideOverlap(_index, countries, retrieval, NullLogger<MainGuideOverlap>.Instance),
            retrieval,
            NullLogger<ClueSampler>.Instance);
    }

    private void Drawn(params KnowledgeHit[] hits) =>
        _index.SampleAsync(default!, default).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<KnowledgeHit>>(hits));

    private static KnowledgeHit Clue(string guide, string text) =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, text, $"https://docs.google.com/presentation/d/{guide}/edit",
            $"https://bot.example/api/v1/ai/images/{guide}.png", "A guide", null, "A guide > Slide 3", null, 0, "gslides", guide, "3-img-1");
}
