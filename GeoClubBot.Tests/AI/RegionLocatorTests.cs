using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The map drawn after an answer: of the country whose regions it names, evenly highlighted, never at the answer's cost.</summary>
public sealed class RegionLocatorTests
{
    private readonly IRegionBoundaries _boundaries = Substitute.For<IRegionBoundaries>();
    private readonly IRegionMapDrawer _drawer = Substitute.For<IRegionMapDrawer>();
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly AiToolsConfiguration _tools = new();

    public RegionLocatorTests()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["vietnam", "malaysia", "thailand"]));
        _boundaries.ReadNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<RegionName>>([]));
        _boundaries.ReadNamesAsync("VN", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<RegionName>>(
        [
            new("VN-37", "Lạng Sơn", ["Lang Son"]), new("VN-13", "Quảng Ninh", ["Quang Ninh"]), new("VN-HN", "Hanoi", [])
        ]));
        _boundaries.ReadNamesAsync("MY", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<RegionName>>(
        [
            new("MY-12", "Sabah", []), new("MY-13", "Sarawak", [])
        ]));
        _drawer.DrawAsync(Arg.Any<RegionMapRequest>(), Arg.Any<CancellationToken>()).Returns("https://bot.example/api/v1/ai/images/map.jpg");
    }

    [Fact]
    public async Task DrawsTheRegionsTheAnswerNames_OnTheirCountry()
    {
        var result = await Locator().LocateAsync(
            "This pattern is typical of far north-eastern Vietnam: Lang Son and Quảng Ninh.", "where is this pole seen?", CancellationToken.None);

        result.Should().NotBeNull();
        result!.CountryName.Should().Be("Vietnam");
        result.Regions.Should().Equal("Lạng Sơn", "Quảng Ninh");
        result.Title.Should().Be("Lạng Sơn and Quảng Ninh in Vietnam");
        result.ImageUrl.Should().Be("https://bot.example/api/v1/ai/images/map.jpg");

        await _drawer.Received(1).DrawAsync(
            Arg.Is<RegionMapRequest>(request => request.CountryCode == "VN"
                                                && request.Regions.Count == 2
                                                && request.Regions.All(region => region.Weight == 1d)
                                                && !request.Shaded),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PicksTheCountryWithMostRegionsNamed_WhenTheAnswerNamesSeveralCountries()
    {
        var result = await Locator().LocateAsync(
            "Unlike Vietnam's Hanoi area, in Malaysia this is Sabah and Sarawak.", null, CancellationToken.None);

        result!.CountryName.Should().Be("Malaysia");
        result.Regions.Should().Equal("Sabah", "Sarawak");
    }

    [Fact]
    public async Task UsesTheQuestionsCountry_WhenTheAnswerNamesNone()
    {
        var result = await Locator().LocateAsync("Mostly Sabah, sometimes Sarawak.", "which part of Malaysia has these?", CancellationToken.None);

        result!.CountryName.Should().Be("Malaysia");
    }

    [Fact]
    public async Task DrawsNothing_WhenTheAnswerNamesNoRegion()
    {
        (await Locator().LocateAsync("It is common all over Vietnam.", null, CancellationToken.None)).Should().BeNull();
        await _drawer.DidNotReceiveWithAnyArgs().DrawAsync(default!, default);
    }

    [Fact]
    public async Task DrawsNothing_WhenNoCountryIsNamedAtAll()
    {
        (await Locator().LocateAsync("Lang Son and Quang Ninh.", null, CancellationToken.None)).Should().BeNull();
        await _boundaries.DidNotReceiveWithAnyArgs().ReadNamesAsync(default!, default);
    }

    [Fact]
    public async Task CapsTheRegionsShown()
    {
        _tools.RegionMap.MaxRegions = 1;

        var result = await Locator().LocateAsync("Vietnam: Lang Son, Quang Ninh and Hanoi.", null, CancellationToken.None);

        result!.Regions.Should().Equal("Lạng Sơn");
    }

    [Fact]
    public async Task DrawsNothing_WhenOff()
    {
        _tools.RegionMap.Enabled = false;

        (await Locator().LocateAsync("Vietnam: Lang Son.", null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NeverFailsTheAnswer()
    {
        _drawer.DrawAsync(Arg.Any<RegionMapRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("A bug."));

        (await Locator().LocateAsync("Vietnam: Lang Son.", null, CancellationToken.None)).Should().BeNull();
    }

    private RegionLocator Locator()
    {
        var retrieval = Options.Create(new AiRetrievalConfiguration());
        var directory = new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);

        return new RegionLocator(_boundaries, _drawer, directory, Options.Create(_tools), TimeProvider.System, NullLogger<RegionLocator>.Instance);
    }
}
