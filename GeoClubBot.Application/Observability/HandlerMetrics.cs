using System.Diagnostics.Metrics;

namespace UseCases.Observability;

/// <summary>
/// Shared meter + instruments for MediatR pipeline metrics. Recorded by
/// <see cref="UseCases.Behaviors.LoggingBehavior{TRequest, TResponse}"/> at every
/// handler invocation. OTLP collectors should subscribe to <see cref="MeterName"/>
/// to scrape these.
/// </summary>
public static class HandlerMetrics
{
    public const string MeterName = "GeoClubBot.Application";

    public static readonly Meter Meter = new(MeterName, version: "1.0.0");

    public static readonly Histogram<double> HandlerDurationMs = Meter.CreateHistogram<double>(
        name: "geoclubbot.handler.duration",
        unit: "ms",
        description: "Duration of a MediatR request handler invocation in milliseconds.");

    public static readonly Counter<long> HandlerFailures = Meter.CreateCounter<long>(
        name: "geoclubbot.handler.failures",
        unit: "{failure}",
        description: "Count of MediatR request handler invocations that threw an exception.");
}
