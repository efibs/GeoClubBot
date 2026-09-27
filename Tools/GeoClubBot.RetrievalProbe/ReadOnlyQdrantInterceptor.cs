using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Hard stop against ever changing the index the probe is pointed at — usually production. Every call
/// the probe makes to Qdrant passes through here, and anything not on a short list of reads throws
/// before it reaches the network.
///
/// An allow-list rather than a block-list on purpose: a Qdrant release that adds another way to write
/// is refused by default, instead of slipping through until someone remembers to block it.
/// </summary>
public sealed class ReadOnlyQdrantInterceptor : Interceptor
{
    /// <summary>Every gRPC method the probe's commands use. All of them only read.</summary>
    public static readonly IReadOnlySet<string> AllowedMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "/qdrant.Qdrant/HealthCheck",
        "/qdrant.Collections/List",
        "/qdrant.Collections/Get",
        "/qdrant.Collections/CollectionExists",
        "/qdrant.Points/Query",
        "/qdrant.Points/QueryBatch",
        "/qdrant.Points/Scroll",
        "/qdrant.Points/Get",
        "/qdrant.Points/Count"
    };

    /// <summary>A Qdrant client whose every call passes the guard.</summary>
    public static QdrantClient Connect(Uri address) =>
        new(new QdrantGrpcClient(GrpcChannel.ForAddress(address).Intercept(new ReadOnlyQdrantInterceptor())));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Guard(context.Method);
        return continuation(request, context);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Guard(context.Method);
        return continuation(request, context);
    }

    // Qdrant's client reads nothing by streaming, so every streaming call is refused outright.

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation) =>
        throw Refused(context.Method);

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) =>
        throw Refused(context.Method);

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation) =>
        throw Refused(context.Method);

    private static void Guard(IMethod method)
    {
        if (!AllowedMethods.Contains(method.FullName))
        {
            throw Refused(method);
        }
    }

    private static InvalidOperationException Refused(IMethod method) =>
        new($"GeoClubBot.RetrievalProbe is read-only: refusing to call {method.FullName}. "
            + "If the index genuinely needs changing, do it deliberately through the bot - not with this tool.");
}
