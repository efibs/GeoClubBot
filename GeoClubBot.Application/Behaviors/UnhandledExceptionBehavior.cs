using MediatR;
using Microsoft.Extensions.Logging;

namespace UseCases.Behaviors;

public partial class UnhandledExceptionBehavior<TRequest, TResponse>(ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogUnhandledException(logger, exception, typeof(TRequest).Name);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception for request {RequestName}")]
    static partial void LogUnhandledException(ILogger logger, Exception exception, string requestName);
}
