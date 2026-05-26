using Microsoft.AspNetCore.Mvc;
using Utilities;

namespace GeoClubBot.Middleware;

public static class ResultExtensions
{
    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller) =>
        result.IsSuccess
            ? controller.Ok(result.Value)
            : controller.ToProblemDetails(result.Error);

    public static IActionResult ToActionResult(this Result result, ControllerBase controller) =>
        result.IsSuccess
            ? controller.NoContent()
            : controller.ToProblemDetails(result.Error);

    public static ObjectResult ToProblemDetails(this ControllerBase controller, Error error)
    {
        var statusCode = StatusCodeFor(error.Type);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = TitleFor(error.Type),
            Detail = error.Message,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = controller.HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;

        return controller.StatusCode(statusCode, problem);
    }

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.NotFound => "Not found",
        ErrorType.Validation => "Validation failed",
        ErrorType.Conflict => "Conflict",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.Unauthorized => "Unauthorized",
        _ => "An unexpected error occurred"
    };
}
