using Infrastructure.OutputAdapters.AI;
using Qdrant.Client;
using Testcontainers.Qdrant;
using Xunit;

namespace GeoClubBot.Tests.Integration;

/// <summary>
/// Spins up a fresh Qdrant container per test-class collection and hands out
/// <see cref="QdrantPlonkItVectorIndex"/> instances bound to a caller-supplied collection
/// name. Tests should take a unique name via <see cref="NewCollectionName"/> so the shared
/// container can be reused without cross-test interference.
/// </summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.15.1").Build();

    private QdrantClient _client = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _client = new QdrantClient(new Uri(_container.GetGrpcConnectionString()));
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    public static string NewCollectionName() => $"plonkit-{Guid.NewGuid():N}";

    public QdrantPlonkItVectorIndex CreateIndex(string collectionName) => new(_client, collectionName);
}

[CollectionDefinition(Name)]
public sealed class QdrantCollection : ICollectionFixture<QdrantFixture>
{
    public const string Name = "Qdrant";
}
