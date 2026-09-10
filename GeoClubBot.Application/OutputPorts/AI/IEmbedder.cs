using Utilities;

namespace UseCases.OutputPorts.AI;

/// <summary>
/// One thing to embed. Text and image are separate cases on purpose — the provider accepts both in a
/// single input block to produce a "joint" embedding, but measurement showed that vector is dominated
/// by the image and loses most of the text signal (0.32 vs 0.73 similarity to a relevant text query).
/// Chunks therefore carry two independent vectors rather than one blended one, and this type makes the
/// blended form unrepresentable.
/// </summary>
public abstract record EmbeddingInput;

public sealed record TextEmbeddingInput(string Text) : EmbeddingInput;

/// <summary>
/// An image to embed. Either a public URL, which the provider fetches server-side — so it must be
/// reachable from the internet by a client that is not a browser, and the whole request fails when it
/// is not — or a <c>data:</c> URL carrying the bytes inline, which needs no fetch at all.
/// </summary>
public sealed record ImageEmbeddingInput(string ImageUrl) : EmbeddingInput;

/// <summary>
/// Codes an <see cref="IEmbedder"/> fails with. Callers act on them differently: a rejection is about
/// the input and will fail the same way again, while a failure is about the moment and is worth retrying.
/// </summary>
public static class EmbeddingErrorCodes
{
    /// <summary>The provider's rate limit. Nothing is wrong with the request except its timing.</summary>
    public const string RateLimited = "ai.rate_limited";

    /// <summary>The provider refused the request as sent — typically an image it could not fetch or read.</summary>
    public const string Rejected = "ai.embedding_rejected";

    /// <summary>The provider could not be reached or failed on its side; the same request may succeed later.</summary>
    public const string Failed = "ai.embedding_failed";
}

/// <summary>
/// Turns text and images into vectors in a single shared space.
///
/// Batching is part of the contract rather than an optimisation: the provider bills and rate-limits
/// per request, so embedding a corpus one chunk at a time would exhaust a daily allowance that
/// batching reduces to a handful of calls.
/// </summary>
public interface IEmbedder
{
    /// <summary>
    /// Model identifier, recorded alongside the index so a model change is detected rather than
    /// silently corrupting retrieval — vectors from different models are not comparable.
    /// </summary>
    string ModelId { get; }

    /// <summary>Vector length this model emits. Must match the collection's configuration.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds inputs in order; the result has one vector per input, index-aligned. Vectors arrive
    /// L2-normalised, so cosine distance can be used without renormalising.
    /// </summary>
    Task<Result<IReadOnlyList<ReadOnlyMemory<float>>>> EmbedAsync(
        IReadOnlyList<EmbeddingInput> inputs,
        CancellationToken cancellationToken = default);
}
