using System.Globalization;
using GeoClubBot.RetrievalProbe;
using Grpc.Core;
using Infrastructure.OutputAdapters.AI;

// GeoClubBot.RetrievalProbe - replays questions through the bot's own retrieval against a knowledge
// index, read-only. See README.md for why it exists and how to point it at production safely.

// Reports are compared across machines; "0,4607" on one and "0.4607" on another helps nobody.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var arguments = ProbeArguments.Parse(args);
if (arguments is null)
{
    ProbeArguments.PrintUsage();
    return 1;
}

if (arguments.Command is not ("replay" or "compare" or "grep" or "similarity"))
{
    Console.Error.WriteLine($"Unknown command '{arguments.Command}'.");
    ProbeArguments.PrintUsage();
    return 1;
}

var settings = ProbeSettings.Resolve(arguments);
if (settings is null)
{
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var qdrant = ReadOnlyQdrantInterceptor.Connect(settings.QdrantAddress);
var report = new ProbeReport();
EmbeddingCache? embeddings = null;

try
{
    var collections = await qdrant.ListCollectionsAsync(cancellation.Token).ConfigureAwait(false);
    if (!collections.Contains(settings.Collection))
    {
        throw new ProbeException(
            $"No collection '{settings.Collection}' at {settings.QdrantAddress}. It has: "
            + (collections.Count == 0 ? "none" : string.Join(", ", collections)) + ". Pass --collection to pick one.");
    }

    var context = new ProbeContext(
        arguments,
        settings,
        qdrant,
        new QdrantKnowledgeIndex(qdrant, settings.Collection, settings.OpenRouter.EmbeddingDimensions),
        Embeddings,
        report,
        cancellation.Token);

    await (arguments.Command switch
    {
        "replay" => ReplayCommand.RunAsync(context),
        "compare" => CompareCommand.RunAsync(context),
        "grep" => GrepCommand.RunAsync(context),
        _ => SimilarityCommand.RunAsync(context)
    }).ConfigureAwait(false);
}
catch (ProbeException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (RpcException ex)
{
    Console.Error.WriteLine($"Qdrant at {settings.QdrantAddress} refused or dropped the call ({ex.StatusCode}: {ex.Status.Detail}).");
    if (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
    {
        Console.Error.WriteLine("Is the tunnel to the index open? See README.md, \"Reaching the index\".");
    }

    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
finally
{
    if (embeddings is not null)
    {
        Console.Error.WriteLine(
            $"OpenRouter: {embeddings.RequestsSent} embedding request(s) this run. Vectors are cached in {embeddings.CachePath}.");
        embeddings.Dispose();
    }
}

if (arguments.OutputPath is not null)
{
    await File.WriteAllTextAsync(arguments.OutputPath, report.ToString(), cancellation.Token).ConfigureAwait(false);
    Console.Error.WriteLine($"Written to {arguments.OutputPath}");
}

return 0;

EmbeddingCache Embeddings()
{
    if (embeddings is not null)
    {
        return embeddings;
    }

    if (settings.ApiKey is null)
    {
        throw new ProbeException(ProbeSettings.MissingKeyHelp);
    }

    Console.Error.WriteLine($"Embedding with the OpenRouter key from {settings.ApiKeySource}.");
    embeddings = EmbeddingCache.ForOpenRouter(
        settings.ApiKey, settings.OpenRouter, Path.Combine(AppContext.BaseDirectory, "embedding-cache.json"));
    return embeddings;
}
