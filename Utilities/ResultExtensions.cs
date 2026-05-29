namespace Utilities;

/// <summary>
/// Functional helpers around <see cref="Result{T}"/>. Both methods short-circuit on
/// failure: <see cref="Match{T, TOut}"/> branches on outcome, <see cref="Map{T, TOut}"/>
/// transforms the success value while preserving the error.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Pattern-match a <see cref="Result{T}"/>. The selected branch's return value is
    /// returned to the caller; the other branch is not invoked.
    /// </summary>
    public static TOut Match<T, TOut>(
        this Result<T> result,
        Func<T, TOut> onSuccess,
        Func<Error, TOut> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);

    /// <summary>
    /// Transform the success value of a <see cref="Result{T}"/> into a new shape.
    /// Failures are propagated unchanged.
    /// </summary>
    public static Result<TOut> Map<T, TOut>(
        this Result<T> result,
        Func<T, TOut> mapper) =>
        result.IsSuccess ? Result<TOut>.Success(mapper(result.Value)) : Result<TOut>.Failure(result.Error);
}
