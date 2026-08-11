using FluentAssertions;
using Xunit;

namespace GeoClubBot.Tests.Integration;

/// <summary>
/// Exercises <see cref="Infrastructure.OutputAdapters.AI.QdrantPlonkItVectorIndex"/> against a real
/// Qdrant container. The index talks to <c>QdrantClient</c>, whose methods are sealed interface
/// implementations and therefore cannot be substituted — a container is the only way to pin the
/// query/scroll mechanics (similarity ordering, limits, payload mapping, scroll pagination).
/// Every test works in its own collection so the shared container stays reusable.
/// </summary>
[Collection(QdrantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QdrantPlonkItVectorIndexIntegrationTests(QdrantFixture fixture)
{
    /// <summary>Mirrors the private <c>VectorSize</c> in the index — the collection is created with it.</summary>
    private const int VectorSize = 1024;

    /// <summary>Points needed to spill past the index's private 100-point scroll page size.</summary>
    private const int PointsSpanningTwoScrollPages = 105;

    /// <summary>Unit vector along a single axis; cosine similarity against a different axis is 0.</summary>
    private static ReadOnlyMemory<float> Axis(int index)
    {
        var vector = new float[VectorSize];
        vector[index] = 1f;
        return vector;
    }

    /// <summary>Unit vector halfway between two axes; cosine similarity against either is ~0.707.</summary>
    private static ReadOnlyMemory<float> Between(int first, int second)
    {
        var vector = new float[VectorSize];
        vector[first] = vector[second] = (float)(1 / Math.Sqrt(2));
        return vector;
    }

    [Fact]
    public async Task EnsureCollectionExists_CreatesTheCollection_AndIsIdempotent()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());

        (await index.CollectionExistsAsync()).Should().BeFalse();

        await index.EnsureCollectionExistsAsync();
        (await index.CollectionExistsAsync()).Should().BeTrue();

        // Second call must be a no-op rather than a "collection already exists" failure.
        await index.EnsureCollectionExistsAsync();
        (await index.CollectionExistsAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCollection_RemovesTheCollection()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        await index.DeleteCollectionAsync();

        (await index.CollectionExistsAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Search_ReturnsNearestSectionsOrderedBySimilarity_AndHonoursTheLimit()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        // Cosine against the Axis(0) query: exact = 1.0, half = ~0.707, orthogonal = 0.0.
        await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(0), "exact", "plonkit.net/exact", "argentina");
        await index.UpsertAsync(Guid.NewGuid().ToString(), Between(0, 1), "half", "plonkit.net/half", "brazil");
        await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(1), "orthogonal", "plonkit.net/orthogonal", "chile");

        var results = await index.SearchAsync(Axis(0), limit: 2);

        results.Should().HaveCount(2, "the limit must be passed through to the query");
        results.Select(r => r.Text).Should().ContainInOrder("exact", "half");

        // The whole payload must survive the round trip, not just the text.
        results[0].Source.Should().Be("plonkit.net/exact");
        results[0].Country.Should().Be("argentina");
    }

    [Fact]
    public async Task Search_OnAnEmptyCollection_ReturnsNoSections()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        var results = await index.SearchAsync(Axis(0), limit: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_WithAnExistingId_ReplacesThePoint_RatherThanAddingASecond()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();
        var id = Guid.NewGuid().ToString();

        await index.UpsertAsync(id, Axis(0), "stale text", "plonkit.net/v1", "argentina");
        await index.UpsertAsync(id, Axis(0), "fresh text", "plonkit.net/v2", "argentina");

        var results = await index.SearchAsync(Axis(0), limit: 10);

        results.Should().ContainSingle();
        results[0].Text.Should().Be("fresh text");
        results[0].Source.Should().Be("plonkit.net/v2");
    }

    [Fact]
    public async Task GetUniqueCountries_Deduplicates_AndSorts()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        // Seeded out of alphabetical order and with repeats, so the test fails if either
        // the sort or the HashSet deduplication is dropped.
        foreach (var country in new[] { "chile", "argentina", "brazil", "chile", "argentina" })
        {
            await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(0), "section", "plonkit.net/s", country);
        }

        var result = await index.GetUniqueCountriesAsync();

        result.Should().Equal("argentina", "brazil", "chile");
    }

    [Fact]
    public async Task GetUniqueCountries_SeesCountriesBeyondTheFirstScrollPage()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        // One distinct country per point: a single 100-point scroll page structurally cannot
        // carry all of them, so this fails unless the NextPageOffset loop runs. Scroll order
        // is by point id, which these random ids leave undefined — hence "more countries than
        // fit on a page" rather than "a country parked on page two".
        var expected = Enumerable.Range(0, PointsSpanningTwoScrollPages).Select(i => $"country-{i:D3}").ToArray();
        foreach (var country in expected.Reverse())
        {
            await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(0), "section", "plonkit.net/s", country);
        }

        var result = await index.GetUniqueCountriesAsync();

        result.Should().Equal(expected);
    }

    [Fact]
    public async Task GetSectionsByCountry_ReturnsEveryMatchAcrossScrollPages_AndExcludesOtherCountries()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();

        for (var i = 0; i < PointsSpanningTwoScrollPages; i++)
        {
            await index.UpsertAsync(
                Guid.NewGuid().ToString(),
                Axis(i % VectorSize),
                $"argentina section {i}",
                $"plonkit.net/argentina/{i}",
                "argentina");
        }

        await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(0), "brazil section", "plonkit.net/brazil", "brazil");

        var result = await index.GetSectionsByCountryAsync("argentina");

        result.Should().HaveCount(PointsSpanningTwoScrollPages);
        result.Should().OnlyContain(s => s.Country == "argentina");
        result.Select(s => s.Text).Should().Contain(["argentina section 0", $"argentina section {PointsSpanningTwoScrollPages - 1}"]);
    }

    [Fact]
    public async Task GetSectionsByCountry_ReturnsNothing_ForAnUnknownCountry()
    {
        var index = fixture.CreateIndex(QdrantFixture.NewCollectionName());
        await index.EnsureCollectionExistsAsync();
        await index.UpsertAsync(Guid.NewGuid().ToString(), Axis(0), "text", "plonkit.net/a", "argentina");

        var result = await index.GetSectionsByCountryAsync("narnia");

        result.Should().BeEmpty();
    }
}
