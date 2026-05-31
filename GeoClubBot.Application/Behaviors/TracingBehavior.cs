using System.Diagnostics;
using MediatR;
using UseCases.Observability;

namespace UseCases.Behaviors;

/// <summary>
/// Opens an <see cref="Activity"/> span per MediatR request so a single trace shows which
/// use case ran and nests the EF Core / outbound HTTP spans underneath it. On failure the
/// span is marked <see cref="ActivityStatusCode.Error"/> and the exception is recorded.
/// Registered as the outermost pipeline behavior so the span wraps logging, validation and
/// the unit of work.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(requestName, ActivityKind.Internal);
        activity?.SetTag("request_name", requestName);

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            throw;
        }
    }
}
