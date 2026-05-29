using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace UseCases.Behaviors;

public partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestId = Guid.NewGuid().ToString("N");

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestName"] = requestName,
            ["RequestId"] = requestId
        });

        LogHandling(logger, requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            LogHandled(logger, requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch
        {
            stopwatch.Stop();
            LogFailed(logger, requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    [LoggerMessage(LogLevel.Debug, "Handling {RequestName}")]
    static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(LogLevel.Debug, "Handled {RequestName} in {ElapsedMilliseconds}ms")]
    static partial void LogHandled(ILogger logger, string requestName, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Debug, "Failed {RequestName} after {ElapsedMilliseconds}ms")]
    static partial void LogFailed(ILogger logger, string requestName, long elapsedMilliseconds);
}
