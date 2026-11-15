using System.Net;
using System.Text;
using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// GBIF's records of several candidates counted in one request, against a response in the shape the
/// live API gave on 2026-11-15 (trimmed): the taxonKey facet lists every candidate — a synonym under
/// its own key — beside their ancestors, which are not candidates and are left out.
/// </summary>
public sealed class GbifPlantRecordCounterTests
{
    private const string Facet = """
        {"offset":0,"limit":0,"endOfRecords":false,"count":84725,"results":[],"facets":[{"field":"TAXON_KEY","counts":[
          {"name":"6","count":84725},{"name":"7707728","count":84725},{"name":"3925","count":81362},
          {"name":"5284982","count":81335},{"name":"2684070","count":3363},{"name":"2684075","count":3363}]}]}
        """;

    [Fact]
    public async Task CountsEveryCandidate_InOneRequest_SynonymsIncluded()
    {
        var handler = new Handler(Facet);

        var result = await Counter(handler).CountAsync([5284982, 2684075, 5285067], new CountryRecordArea("us"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new Dictionary<long, long> { [5284982] = 81_335, [2684075] = 3_363 });
        handler.Requests.Should().ContainSingle().Which.Should().Be(
            "https://gbif.test/v1/occurrence/search?limit=0&occurrenceStatus=PRESENT&taxonKey=2684075&taxonKey=5284982&taxonKey=5285067"
            + "&facet=taxonKey&facetLimit=1000&country=US");
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void FiltersByTheArea(RecordArea area, string filter) =>
        GbifPlantRecordCounter.AreaFilter(area).Should().Be(filter);

    public static TheoryData<RecordArea, string> Areas() => new()
    {
        { new CountryRecordArea("BG"), "&country=BG" },
        { new CircleRecordArea(42.35843, -71.05977, 50), "&geoDistance=42.3584,-71.0598,50km" },
        { new BoxRecordArea(-73.51, 41.24, -69.93, 42.89), "&decimalLatitude=41.24,42.89&decimalLongitude=-73.51,-69.93" }
    };

    [Fact]
    public async Task AsksTheSameCountOnce_AndNeverKeepsAFailure()
    {
        var handler = new Handler(Facet) { Failure = new HttpRequestException("down") };
        var counter = Counter(handler);

        var failed = await counter.CountAsync([5284982], new CountryRecordArea("US"));
        failed.Error.Type.Should().Be(ErrorType.Unexpected);

        handler.Failure = null;
        (await counter.CountAsync([5284982], new CountryRecordArea("US"))).IsSuccess.Should().BeTrue();
        (await counter.CountAsync([5284982], new CountryRecordArea("US"))).IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2, "the failure is asked again, the answer is not");
    }

    [Fact]
    public async Task AsksNothing_ForNoCandidates()
    {
        var handler = new Handler(Facet);

        (await Counter(handler).CountAsync([], new CountryRecordArea("US"))).Value.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    private static GbifPlantRecordCounter Counter(Handler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GbifSpeciesRangeReader.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new GbifPlantRecordCounter(
            factory,
            Options.Create(new AiToolsConfiguration { SpeciesRange = { BaseUrl = "https://gbif.test/" } }),
            TimeProvider.System,
            NullLogger<GbifPlantRecordCounter>.Instance);
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public Exception? Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
