namespace Utilities;

public enum ErrorType
{
    Unexpected = 0,
    NotFound,
    Validation,
    Conflict,
    Forbidden,
    Unauthorized,
}

public readonly record struct Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Unexpected(string code, string message) => new(code, message, ErrorType.Unexpected);
}

public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = Error.None;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        if (error == Error.None)
            throw new ArgumentException("A failed result requires a non-default error.", nameof(error));

        _value = default;
        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access Value on a failed result.");

    public T? ValueOrNull => IsSuccess ? _value : default;

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) =>
        error == Error.None
            ? throw new ArgumentException("A failed result requires a non-default error.", nameof(error))
            : new Result(false, error);

    public static implicit operator Result(Error error) => Failure(error);
}
