using Entities.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DataAnnotationsValidationException = System.ComponentModel.DataAnnotations.ValidationException;
using FluentValidationException = FluentValidation.ValidationException;

namespace GeoClubBot.Middleware;

public partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            FluentValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            DataAnnotationsValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Unprocessable entity"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, exception, httpContext.Request.Path);
        }
        else
        {
            LogRequestFailed(logger, exception, statusCode, httpContext.Request.Path);
        }

        ProblemDetails problemDetails;
        if (exception is FluentValidationException fluentValidationException)
        {
            var errors = fluentValidationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            problemDetails = new ValidationProblemDetails(errors)
            {
                Status = statusCode,
                Title = title,
                Type = $"https://httpstatuses.com/{statusCode}",
                Instance = httpContext.Request.Path
            };
        }
        else
        {
            problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message,
                Type = $"https://httpstatuses.com/{statusCode}",
                Instance = httpContext.Request.Path
            };
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken).ConfigureAwait(false);

        return true;
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception for {Path}")]
    static partial void LogUnhandledException(ILogger<GlobalExceptionHandler> logger, Exception exception, PathString path);

    [LoggerMessage(LogLevel.Warning, "Request failed with {StatusCode} for {Path}")]
    static partial void LogRequestFailed(ILogger<GlobalExceptionHandler> logger, Exception exception, int statusCode, PathString path);
}
