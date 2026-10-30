namespace Infrastructure.OutputAdapters.Http;

/// <summary>
/// A request the resilience pipeline refused to send because of the host's recent behaviour — an open
/// circuit breaker or a full rate-limit queue — rather than anything about the request itself.
///
/// Still an <see cref="HttpRequestException"/>, so every adapter that catches "the far side could not be
/// reached" keeps catching it. The distinction is for the guide extractors: a refused request says
/// nothing about the guide that was asked for, and recording it as that guide's failure is what backed
/// off every remaining source of a run once one host had tripped the breaker they all share.
/// </summary>
public sealed class HostRefusedRequestException(string message, Exception innerException)
    : HttpRequestException(message, innerException);
