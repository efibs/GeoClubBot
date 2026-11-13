namespace Infrastructure.OutputAdapters.Http;

/// <summary>
/// A response whose headers arrived and whose body then did not finish in time
/// (<see cref="ResponseBodyDeadlineHandler"/>).
///
/// Still an <see cref="HttpRequestException"/>, so every adapter that catches "the far side could not
/// be reached" keeps catching it. The distinction is for the chat client: the provider was reached and
/// answered, so what stalled is the model that was asked, and other models are worth asking.
/// </summary>
public sealed class ResponseBodyTimeoutException(string message, Exception innerException)
    : HttpRequestException(message, innerException);
