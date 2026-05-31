using System.Diagnostics;

namespace UseCases.Observability;

/// <summary>
/// Shared <see cref="ActivitySource"/> for Application-layer tracing. The
/// <see cref="UseCases.Behaviors.TracingBehavior{TRequest, TResponse}"/> opens one span per
/// MediatR request so a trace shows which use case ran. Tracer providers subscribe to
/// <see cref="ActivitySourceName"/> via <c>AddSource(...)</c>.
/// </summary>
public static class ApplicationDiagnostics
{
    public const string ActivitySourceName = "GeoClubBot.Application";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, version: "1.0.0");
}
