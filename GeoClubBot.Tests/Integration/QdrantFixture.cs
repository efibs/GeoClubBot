using Docker.DotNet.Models;
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
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.15.1")
        // Qdrant opens a large number of RocksDB files per collection, and more again per payload
        // index. With a collection per test the default file-descriptor limit is exhausted part-way
        // through a run and later tests fail with "Too many open files". compose.yaml raises the same
        // limit for the same reason.
        .WithCreateParameterModifier(parameters =>
        {
            parameters.HostConfig ??= new HostConfig();
            parameters.HostConfig.Ulimits = [new Ulimit { Name = "nofile", Soft = 65536, Hard = 65536 }];
        })
        .Build();

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

    public QdrantKnowledgeIndex CreateKnowledgeIndex(string collectionName, int vectorSize) =>
        new(_client, collectionName, vectorSize);
}

[CollectionDefinition(Name)]
public sealed class QdrantCollection : ICollectionFixture<QdrantFixture>
{
    public const string Name = "Qdrant";
}
