using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Reading the index's country names scrolls every point in it — about 28,600 on production — so it
/// must happen once per cache period, not once per question, and a failure must never fail a question.
/// </summary>
public sealed class KnowledgeCountryDirectoryTests
{
    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ReadAsync_ReadsTheIndexOnce_PerCachePeriod()
    {
        ArrangeCountries("usa", "united states");
        var directory = CreateDirectory(cacheHours: 6);

        (await directory.ReadAsync()).FindMentions("the USA").Should().ContainSingle();
        _time.Now += TimeSpan.FromHours(5);
        await directory.ReadAsync();

        await _index.Received(1).ListCountriesAsync(Arg.Any<CancellationToken>());

        _time.Now += TimeSpan.FromHours(2);
        await directory.ReadAsync();

        await _index.Received(2).ListCountriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_KeepsTheLastNames_WhenARefreshFails()
    {
        ArrangeCountries("brazil");
        var directory = CreateDirectory(cacheHours: 1);
        await directory.ReadAsync();

        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Qdrant is down"));
        _time.Now += TimeSpan.FromHours(2);

        (await directory.ReadAsync()).FindMentions("Brazilian poles").Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_BoostsNothing_WhenTheNamesWereNeverRead_AndTriesAgainAMinuteLater()
    {
        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Qdrant is down"));
        var directory = CreateDirectory(cacheHours: 6);

        (await directory.ReadAsync()).Names.Should().BeEmpty();
        await directory.ReadAsync();
        await _index.Received(1).ListCountriesAsync(Arg.Any<CancellationToken>());

        ArrangeCountries("brazil");
        _time.Now += TimeSpan.FromMinutes(2);

        (await directory.ReadAsync()).FindMentions("Brazil").Should().ContainSingle();
    }

    [Fact]
    public async Task ReadAsync_AddsTheConfiguredAliases()
    {
        ArrangeCountries("malaysia");
        var settings = new AiRetrievalConfiguration();
        settings.CountryBoost.Aliases["peninsular"] = ["Malaysia"];

        var directory = new KnowledgeCountryDirectory(
            _index, Options.Create(settings), _time, NullLogger<KnowledgeCountryDirectory>.Instance);

        (await directory.ReadAsync()).FindMentions("peninsular lamps").Should().ContainSingle();
    }

    private void ArrangeCountries(params string[] countries) =>
        _index.ListCountriesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>(countries));

    private KnowledgeCountryDirectory CreateDirectory(int cacheHours)
    {
        var settings = new AiRetrievalConfiguration();
        settings.CountryBoost.CountryListCacheHours = cacheHours;

        return new KnowledgeCountryDirectory(
            _index, Options.Create(settings), _time, NullLogger<KnowledgeCountryDirectory>.Instance);
    }
}
