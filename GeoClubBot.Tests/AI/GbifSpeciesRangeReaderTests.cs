using System.Net;
using System.Text;
using Configuration;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Infrastructure.OutputAdapters.AI.Gbif;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.AI;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The GBIF adapter against canned responses captured from the live API on 2026-10-25 (trimmed): the
/// three reads it makes, the order it makes them in, and that nothing it meets is thrown at the answer.
/// </summary>
public sealed class GbifSpeciesRangeReaderTests
{
    private const string KesiyaMatch = """
        {"usageKey":5285824,"scientificName":"Pinus kesiya Royle ex Gordon","canonicalName":"Pinus kesiya","rank":"SPECIES",
         "status":"ACCEPTED","confidence":99,"matchType":"EXACT","kingdom":"Plantae"}
        """;

    private const string KesiyaCountries = """
        {"offset":0,"limit":0,"endOfRecords":false,"count":1061,"results":[],"facets":[{"field":"COUNTRY","counts":[
          {"name":"PH","count":256},{"name":"CN","count":105},{"name":"TH","count":87},{"name":"IN","count":80},{"name":"ZZ","count":26}]}]}
        """;

    /// <summary>What the backbone answers a common name: only the kingdom it was told to prefer.</summary>
    private const string KingdomMatch = """
        {"usageKey":6,"scientificName":"Plantae","canonicalName":"Plantae","rank":"KINGDOM","status":"ACCEPTED","confidence":99,"matchType":"HIGHERRANK"}
        """;

    [Fact]
    public async Task Reads_AScientificName_AndCountsItsRecordsByCountry()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", KesiyaMatch)
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            TaxonKey = 5285824L,
            ScientificName = "Pinus kesiya Royle ex Gordon",
            CanonicalName = "Pinus kesiya",
            Rank = "SPECIES",
            MatchedByCommonName = false,
            FuzzyMatch = false,
            CountryCode = (string?)null,
            TotalRecords = 1061L
        });
        result.Value.Areas.Select(area => (area.Name, area.Records)).Should().StartWith(("PH", 256L));

        handler.Requests.Should().HaveCount(2, "a scientific name needs no vernacular search");
        handler.Requests[0].Should().Contain("name=Pinus%20kesiya").And.Contain("kingdom=Plantae");
        handler.Requests[1].Should().Contain("taxonKey=5285824").And.Contain("facet=country").And.Contain("limit=0")
            .And.Contain("occurrenceStatus=PRESENT").And.NotContain("country=", "every country is counted");
    }

    [Fact]
    public async Task Searches_TheCommonNames_WhenTheMatchFindsOnlyAKingdom()
    {
        // The insect really comes first; its common names are made up here, to show that only a plant counts.
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/search", """
                {"results":[
                  {"key":8475796,"nubKey":8475796,"scientificName":"Inocellia indica Liu & Hajong, 2015","rank":"SPECIES","kingdom":"Animalia",
                   "vernacularNames":[{"vernacularName":"khasi pine"},{"vernacularName":"khasi pine"},{"vernacularName":"khasi pine"},
                                      {"vernacularName":"khasi pine"},{"vernacularName":"khasi pine"},{"vernacularName":"khasi pine"}]},
                  {"key":5285824,"nubKey":5285824,"scientificName":"Pinus kesiya Royle ex Gordon","canonicalName":"Pinus kesiya","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"Khasi pine"},{"vernacularName":"Khasi Pine"},{"vernacularName":"Khasi pine"},{"vernacularName":"Khasi Pine"},
                                      {"vernacularName":"kashiya-matsu"},{"vernacularName":"pin à trois feuilles"}]}]}
                """)
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("khasi pine", null));

        result.Value.TaxonKey.Should().Be(5285824, "a plant is preferred over an insect of the same name");
        result.Value.MatchedByCommonName.Should().BeTrue();
        handler.Requests[1].Should().Contain("q=khasi%20pine").And.Contain("qField=VERNACULAR").And.Contain("rank=SPECIES")
            .And.Contain("datasetKey=d7dddbf4-2cf0-4f39-9b2a-bb099caae36c");
    }

    /// <summary>
    /// GBIF's search matches words anywhere in a name, so a page for a crop is often all namesakes and
    /// pests. Captured 2026-10-25 for "coconut" (five viruses) and "rice" (Acacia riceana, "Rice's
    /// wattle", then viruses and a cockroach): both used to be answered with the first of them.
    /// </summary>
    [Theory]
    [InlineData("coconut", """
        {"results":[
          {"key":9935211,"nubKey":9935211,"scientificName":"Coconut tinangaja viroid","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":10353197,"nubKey":10353197,"scientificName":"Coconut cadang-cadang viroid","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":11520054,"nubKey":11520054,"scientificName":"Coconut foliar decay alphasatellite 7","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":9911822,"nubKey":9911822,"scientificName":"Coconut foliar decay virus","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":11825452,"nubKey":11825452,"scientificName":"Coconut foliar decay alphasatellite 4","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]}]}
        """)]
    [InlineData("rice", """
        {"results":[
          {"key":2980923,"nubKey":2980923,"scientificName":"Acacia riceana Hensl.","canonicalName":"Acacia riceana","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Mimosa de Ricé"},{"vernacularName":"Rice's wattle"},{"vernacularName":"Rice's wattle"}]},
          {"key":10103922,"nubKey":10103922,"scientificName":"Rice dwarf virus","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":7900664,"nubKey":7900664,"scientificName":"Arenivaga ricei Hopkins, 2014","canonicalName":"Arenivaga ricei","rank":"SPECIES","kingdom":"Animalia","vernacularNames":[]},
          {"key":10577733,"nubKey":10577733,"scientificName":"Rice stripe tenuivirus","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]},
          {"key":10136602,"nubKey":10136602,"scientificName":"Rice necrosis mosaic virus","rank":"SPECIES","kingdom":"Viruses","vernacularNames":[]}]}
        """)]
    public async Task ReportsNotFound_RatherThanANamesake_WhenNoPlantGoesByTheName(string name, string search)
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/search", search)
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery(name, null));

        result.Error.Type.Should().Be(ErrorType.NotFound, "a wrong plant's countries would be cited as the answer");
        handler.Requests.Should().NotContain(request => request.Contains("/v1/occurrence/search"));
    }

    /// <summary>
    /// A common name is the name of several plants. Captured 2026-10-25 (trimmed to the entries that
    /// matter): Cerbera inflata comes first for "rubber tree" and is called so twice, Hevea brasiliensis
    /// five times; a snail and a goldenaster come before Yucca brevifolia for "joshua tree", which is
    /// spelt with a hyphen in some of its entries and also counts its synonym Yucca jaegeriana.
    /// </summary>
    [Theory]
    [InlineData("rubber tree", 3071171L, """
        {"results":[
          {"key":5535867,"nubKey":5535867,"scientificName":"Cerbera inflata S.T.Blake","canonicalName":"Cerbera inflata","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Cassowary Plum"},{"vernacularName":"Rubber Tree"},{"vernacularName":"Milkwood"},{"vernacularName":"Rubber Tree"}]},
          {"key":2574966,"nubKey":2574966,"scientificName":"Pestalotiopsis microspora (Speg.) G.C.Zhao & Nan Li","rank":"SPECIES","kingdom":"Fungi",
           "vernacularNames":[{"vernacularName":"Rubber tree leaf blight"}]},
          {"key":2984558,"nubKey":2984558,"scientificName":"Castilla elastica Sessé","canonicalName":"Castilla elastica","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Mexican rubber tree"},{"vernacularName":"Bidzi Vonga"}]},
          {"key":3071171,"nubKey":3071171,"scientificName":"Hevea brasiliensis (Willd. ex A.Juss.) Müll.Arg.","canonicalName":"Hevea brasiliensis","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Rubber Tree"},{"vernacularName":"Hévéa du Brésil, Arbre à caoutchouc"},{"vernacularName":"Rubber Tree"},
                              {"vernacularName":"Rubber Tree"},{"vernacularName":"rubber tree"},{"vernacularName":"rubber tree"}]},
          {"key":7531920,"nubKey":7531920,"scientificName":"Castilla elastica Cerv.","canonicalName":"Castilla elastica","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Rubber Tree"},{"vernacularName":"Palo de Hule"},{"vernacularName":"Rubber Tree"}]},
          {"key":3069353,"nubKey":3069353,"scientificName":"Euphorbia tirucalli L.","canonicalName":"Euphorbia tirucalli","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Rubber Tree"},{"vernacularName":"Rubber Tree"}]}]}
        """)]
    [InlineData("joshua tree", 2775592L, """
        {"results":[
          {"key":2296111,"nubKey":2296111,"scientificName":"Sonorelix rixfordi (Pilsbry, 1919)","rank":"SPECIES","kingdom":"Animalia",
           "vernacularNames":[{"vernacularName":"Joshua Tree desertsnail"},{"vernacularName":"Joshua Tree desertsnail"}]},
          {"key":11202492,"nubKey":11202492,"scientificName":"Heterotheca joshuana G.L.Nesom","canonicalName":"Heterotheca joshuana","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Joshua Tree Goldenaster"}]},
          {"key":2775592,"nubKey":2775592,"scientificName":"Yucca brevifolia Engelm.","canonicalName":"Yucca brevifolia","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Western Joshua Tree"},{"vernacularName":"Joshua-tree"},{"vernacularName":"Joshua-tree"}]},
          {"key":2775598,"nubKey":2775598,"acceptedKey":2775592,"scientificName":"Yucca jaegeriana (McKelvey) L.W.Lenz","canonicalName":"Yucca jaegeriana","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Eastern Joshua Tree"},{"vernacularName":"Joshua Tree"}]},
          {"key":7390928,"nubKey":7390928,"acceptedKey":2775790,"scientificName":"Yucca brevifolia Schott ex Trel.","canonicalName":"Yucca brevifolia","rank":"SPECIES","kingdom":"Plantae",
           "vernacularNames":[{"vernacularName":"Joshua tree"}]}]}
        """)]
    public async Task TakesThePlantCalledSoMostOften_NotTheFirstToCarryTheName(string name, long expectedKey, string search)
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/search", search)
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery(name, null));

        result.Value.TaxonKey.Should().Be(expectedKey);
        result.Value.MatchedByCommonName.Should().BeTrue();
        handler.Requests[^1].Should().Contain($"taxonKey={expectedKey}");
    }

    [Fact]
    public async Task ReportsNotFound_WhenSeveralPlantsShareTheNameEqually()
    {
        // Captured 2026-10-25 for "flame tree": two plants called so twice each, another once.
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/search", """
                {"results":[
                  {"key":8119595,"nubKey":8119595,"scientificName":"Brachychiton acerifolius (A.Cunn. ex G.Don) F.Muell.","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"flame tree"},{"vernacularName":"Illawarra Flame Tree"},{"vernacularName":"flame tree"}]},
                  {"key":8170495,"nubKey":8170495,"scientificName":"Brownea macrophylla Linden ex Mast.","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"flame tree"}]},
                  {"key":2956176,"nubKey":2956176,"scientificName":"Delonix regia (Bojer ex Hook.) Raf.","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"Flame Tree"},{"vernacularName":"Royal poinciana"},{"vernacularName":"Flame Tree"}]}]}
                """);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("flame tree", null));

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ReportsNotFound_WhenOnlyOneSourceUsesTheName()
    {
        // Captured 2026-10-25: Agave cacozela is "Bamboo" in one Spanish checklist, indexed twice.
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/search", """
                {"results":[
                  {"key":2767080,"nubKey":2767080,"scientificName":"Agave cacozela Trel.","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"Bamboo"},{"vernacularName":"Manilla"},{"vernacularName":"Manilla"},{"vernacularName":"Bamboo"}]}]}
                """);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("bamboo", null));

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// GBIF reads one word as a genus only when it is capitalised. Captured 2026-10-25: "eucalyptus"
    /// matches the kingdom, and its vernacular page starts with a hybrid, which used to be the answer;
    /// "Eucalyptus" is the genus.
    /// </summary>
    [Fact]
    public async Task MatchesALowerCaseGenus_ByItsCapitalisedName()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/match?name=Eucalyptus&", """
                {"usageKey":7493935,"scientificName":"Eucalyptus L'Hér.","canonicalName":"Eucalyptus","rank":"GENUS","status":"ACCEPTED","confidence":95,"matchType":"EXACT"}
                """)
            .On("/v1/species/search", """
                {"results":[
                  {"key":11466690,"nubKey":11466690,"scientificName":"Eucalyptus coccifera x Eucalyptus delegatensis","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"Hybride entre l'Eucalyptus à baies et l'Eucalyptus de Delegate"}]}]}
                """)
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("eucalyptus", null));

        result.Value.Should().BeEquivalentTo(new { TaxonKey = 7493935L, Rank = "GENUS", MatchedByCommonName = false, FuzzyMatch = false });
        handler.Requests.Should().HaveCount(2, "the genus needs no vernacular search");
    }

    /// <summary>
    /// Captured 2026-10-25: the backbone holds a placeholder "Adansonia spec", so the genus itself comes
    /// back as a higher-rank match. "Coca" comes back the same way, as the unrelated genus Cota.
    /// </summary>
    [Theory]
    [InlineData("adansonia", """{"usageKey":3152213,"scientificName":"Adansonia L.","canonicalName":"Adansonia","rank":"GENUS","status":"ACCEPTED","matchType":"HIGHERRANK","confidence":95}""", 3152213L)]
    [InlineData("coca", """{"usageKey":7925185,"scientificName":"Cota J.Gay","canonicalName":"Cota","rank":"GENUS","status":"ACCEPTED","matchType":"HIGHERRANK","confidence":80}""", null)]
    public async Task TakesAHigherRankMatch_OnlyWhenItIsTheGenusAsked(string name, string match, long? expectedKey)
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", match)
            .On("/v1/species/search", """{"results":[]}""")
            .On("/v1/occurrence/search", KesiyaCountries);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery(name, null));

        if (expectedKey is { } key)
        {
            result.Value.TaxonKey.Should().Be(key);
        }
        else
        {
            result.Error.Type.Should().Be(ErrorType.NotFound);
        }
    }

    /// <summary>
    /// Captured 2026-10-25: capitalised, "banana" is read approximately as Banara (a willow relative),
    /// so one word read approximately goes to the common names, where Musa ×paradisiaca is "banana"
    /// five times and Musa acuminata four. A misspelt binomial is still read approximately.
    /// </summary>
    [Fact]
    public async Task ReadsOneWordAsACommonName_RatherThanAsAMisspeltGenus()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", KingdomMatch)
            .On("/v1/species/match?name=Banana&", """
                {"usageKey":2874154,"scientificName":"Banara Aubl.","canonicalName":"Banara","rank":"GENUS","status":"ACCEPTED","confidence":85,"matchType":"FUZZY"}
                """)
            .On("/v1/species/search", """
                {"results":[
                  {"key":5208771,"nubKey":5208771,"scientificName":"Awaous banana (Valenciennes, 1837)","rank":"SPECIES","kingdom":"Animalia",
                   "vernacularNames":[{"vernacularName":"River gobie"}]},
                  {"key":2762680,"nubKey":2762680,"scientificName":"Musa acuminata Colla","canonicalName":"Musa acuminata","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"Edible Banana"}]},
                  {"key":2762752,"nubKey":2762752,"scientificName":"Musa ×paradisiaca L.","canonicalName":"Musa paradisiaca","rank":"SPECIES","kingdom":"Plantae",
                   "vernacularNames":[{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"banana"},{"vernacularName":"bananeira"}]}]}
                """)
            .On("/v1/occurrence/search", KesiyaCountries);
        var reader = CreateReader(handler);

        var banana = await reader.ReadAsync(new SpeciesRangeQuery("banana", null));

        banana.Value.TaxonKey.Should().Be(2762752);
        banana.Value.MatchedByCommonName.Should().BeTrue();

        handler.On("/v1/species/match?name=Pinus%20kesia&", """
            {"usageKey":5285824,"scientificName":"Pinus kesiya Royle ex Gordon","canonicalName":"Pinus kesiya","rank":"SPECIES","status":"ACCEPTED","confidence":95,"matchType":"FUZZY"}
            """);
        var misspelt = await reader.ReadAsync(new SpeciesRangeQuery("pinus kesia", null));

        misspelt.Value.Should().BeEquivalentTo(new { TaxonKey = 5285824L, FuzzyMatch = true, MatchedByCommonName = false });
    }

    [Fact]
    public async Task CountsTheRegionsOfOneCountry_WhenOneIsGiven()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", """{"usageKey":2775707,"scientificName":"Yucca filifera Chabaud","canonicalName":"Yucca filifera","rank":"SPECIES","matchType":"EXACT"}""")
            .On("/v1/occurrence/search", """
                {"count":1609,"facets":[{"field":"STATE_PROVINCE","counts":[{"name":"Nuevo León","count":384},{"name":"Zacatecas","count":247}]}]}
                """);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Yucca filifera", "mx"));

        result.Value.CountryCode.Should().Be("MX");
        result.Value.Areas.Select(area => area.Name).Should().Equal("Nuevo León", "Zacatecas");
        handler.Requests[1].Should().Contain("taxonKey=2775707").And.Contain("country=MX").And.Contain("facet=stateProvince");
    }

    [Fact]
    public async Task FollowsASynonym_ToTheNameItStandsFor()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", """{"usageKey":111,"acceptedUsageKey":222,"scientificName":"Old name","rank":"SPECIES","matchType":"EXACT","status":"SYNONYM"}""")
            .On("/v1/occurrence/search", """{"count":0,"facets":[]}""");

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Old name", null));

        result.Value.TaxonKey.Should().Be(222);
        result.Value.Areas.Should().BeEmpty();
    }

    [Fact]
    public async Task ReportsNotFound_WhenNoPlantGoesByTheName()
    {
        var handler = new RoutingHandler()
            .On("/v1/species/match", """{"matchType":"NONE","confidence":100}""")
            .On("/v1/species/search", """{"results":[]}""");

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("glorbleweed", null));

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReportsTheDatabaseUnavailable_RatherThanThrowing(HttpStatusCode status)
    {
        var handler = new RoutingHandler().On("/v1/species/match", "{}", status);

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));

        result.Error.Code.Should().Be("ai.species_range_unavailable");
    }

    [Fact]
    public async Task ReportsTheDatabaseUnavailable_WhenItsReplyIsNotJson()
    {
        var handler = new RoutingHandler().On("/v1/species/match", "<html>maintenance</html>");

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));

        result.Error.Code.Should().Be("ai.species_range_unavailable");
    }

    [Fact]
    public async Task ReportsTheDatabaseUnavailable_WhenTheConnectionFails()
    {
        var handler = new RoutingHandler { Failure = new HttpRequestException("Name or service not known") };

        var result = await CreateReader(handler).ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));

        result.Error.Code.Should().Be("ai.species_range_unavailable");
    }

    [Fact]
    public async Task KeepsAnAnswer_ForTheConfiguredHours_ButNeverAFailure()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var handler = new RoutingHandler().On("/v1/species/match", "{}", HttpStatusCode.ServiceUnavailable);
        var reader = CreateReader(handler, clock);

        (await reader.ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null))).IsFailure.Should().BeTrue();

        handler.On("/v1/species/match", KesiyaMatch).On("/v1/occurrence/search", KesiyaCountries);
        (await reader.ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null))).IsSuccess.Should().BeTrue("a failure is asked again");
        var asked = handler.Requests.Count;

        (await reader.ReadAsync(new SpeciesRangeQuery(" pinus KESIYA ", null))).IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(asked, "the same plant is answered from memory");

        clock.Now += TimeSpan.FromHours(25);
        await reader.ReadAsync(new SpeciesRangeQuery("Pinus kesiya", null));
        handler.Requests.Count.Should().BeGreaterThan(asked, "after a day the counts are read again");
    }

    private static GbifSpeciesRangeReader CreateReader(RoutingHandler handler, TimeProvider? clock = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GbifSpeciesRangeReader.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new GbifSpeciesRangeReader(
            factory,
            Options.Create(new AiToolsConfiguration { SpeciesRange = { BaseUrl = "https://gbif.test" } }),
            clock ?? TimeProvider.System,
            NullLogger<GbifSpeciesRangeReader>.Instance);
    }

    /// <summary>
    /// Answers each request with the canned body of the longest route its path and query start with —
    /// a path alone, or a path and the start of a query — and records every URL asked for.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Status)> _routes = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public Exception? Failure { get; init; }

        public RoutingHandler On(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[path] = (body, status);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            if (Failure is not null)
            {
                throw Failure;
            }

            var pathAndQuery = request.RequestUri.PathAndQuery;
            var route = _routes
                .Where(candidate => candidate.Key == request.RequestUri.AbsolutePath
                                    || (candidate.Key.Contains('?') && pathAndQuery.StartsWith(candidate.Key, StringComparison.Ordinal)))
                .OrderByDescending(candidate => candidate.Key.Length)
                .Select(candidate => candidate.Value)
                .FirstOrDefault(("{}", HttpStatusCode.NotFound));
            var (body, status) = route;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
