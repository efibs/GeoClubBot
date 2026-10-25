using System.Net;
using Infrastructure.OutputAdapters.Http;
using UseCases.OutputPorts.AI.Ingestion;
using Utilities;

namespace Infrastructure.OutputAdapters.AI.Extractors;

/// <summary>
/// What every extractor does with a fetch that went wrong, and how the heavy exports are downloaded.
///
/// One classification for all of them, because each used to keep its own and they disagreed: the
/// Docs extractor took 404 as "not public" and 410 as worth retrying, Slides and Sheets retried 413,
/// and the extractors that call <see cref="HttpClient.GetStringAsync(Uri, CancellationToken)"/> could
/// not see a status at all, so a deleted imgur album was retried every few nights for ever. The run
/// treats each outcome differently (see <see cref="SourceExtractionErrorCodes"/>), which only works if
/// every extractor means the same thing by it. The one status whose meaning depends on the host — an
/// access refusal — is named by each extractor through <see cref="ForbiddenMeans"/>.
/// </summary>
internal static class SourceFetch
{
    /// <summary>Bytes read per call while downloading an export.</summary>
    private const int ReadWindowBytes = 81920;

    /// <param name="what">The kind of source, as it reads in "this {what} no longer exists".</param>
    /// <param name="forbidden">What a 401 or 403 from this source's host says; see <see cref="ForbiddenMeans"/>.</param>
    public static Error FromStatus(HttpStatusCode status, string what, ForbiddenMeans forbidden)
    {
        var code = (int)status;

        return code switch
        {
            // Measured on production: a deleted Google file answers 410 on every endpoint, deterministically.
            404 or 410 => Error.NotFound(SourceExtractionErrorCodes.Gone,
                $"This {what} no longer exists (HTTP {code})."),

            401 or 403 when forbidden == ForbiddenMeans.HostRefused => Error.Conflict(SourceExtractionErrorCodes.HostRefused,
                $"The host of this {what} is turning the bot away (HTTP {code})."),

            401 or 403 => Error.Forbidden(SourceExtractionErrorCodes.NotPublic,
                $"This {what} is not publicly readable (HTTP {code})."),

            // Google spends the best part of a minute building an export it then refuses.
            413 => Error.Validation(SourceExtractionErrorCodes.ExportTooLarge,
                $"This {what} is too large for its host to export (HTTP 413)."),

            429 => Error.Conflict(SourceExtractionErrorCodes.HostRefused,
                $"The host of this {what} is refusing requests (HTTP 429)."),

            _ => Error.Unexpected(SourceExtractionErrorCodes.Unreachable,
                $"Could not fetch this {what} (HTTP {code}).")
        };
    }

    /// <summary>
    /// Classifies an exception thrown by a fetch: a status carried by the exception (what
    /// <c>GetStringAsync</c> throws on a non-success answer), a request the pipeline refused on the
    /// host's account, or a timeout and anything else, which is about the moment.
    /// </summary>
    public static Error FromException(Exception exception, string what, ForbiddenMeans forbidden) => exception switch
    {
        HostRefusedRequestException refused => Error.Conflict(SourceExtractionErrorCodes.HostRefused,
            $"The host of this {what} is refusing requests: {refused.Message}"),

        HttpRequestException { StatusCode: { } status } => FromStatus(status, what, forbidden),

        _ => Error.Unexpected(SourceExtractionErrorCodes.Unreachable,
            $"Could not fetch this {what}: {exception.Message}")
    };

    /// <summary>
    /// Whether an exception is a failed fetch to report, rather than the caller giving up — a shutdown
    /// mid-run must stop the run, not be recorded as every remaining source's failure.
    ///
    /// Broader than "the request failed" because a heavy export spends most of its time in the body,
    /// and a body that is cut off or runs past the download's own deadline fails as an I/O error or a
    /// plain cancellation rather than as an <see cref="HttpRequestException"/>. Escaping the extractor,
    /// either one ended the whole run instead of failing one source.
    /// </summary>
    public static bool IsFetchFailure(Exception exception, CancellationToken callerToken) =>
        (exception is HttpRequestException or OperationCanceledException or IOException)
        && !callerToken.IsCancellationRequested;

    /// <summary>
    /// Reads the body, giving up once it passes <paramref name="maxBytes"/>.
    ///
    /// Exports arrive chunked, so their size is never announced and a header check would let them
    /// through: the only way to find out is to read until it is too much. Buffering the lot is what this
    /// avoids — one guide's export is a third of a gigabyte, in a container with a memory limit. The
    /// response must have been requested with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, or
    /// HttpClient has already buffered it whole before this is called.
    /// </summary>
    /// <returns>The body, positioned at its start; the caller disposes it.</returns>
    public static async Task<Result<MemoryStream>> ReadCappedAsync(
        HttpResponseMessage response,
        long maxBytes,
        string what,
        CancellationToken cancellationToken)
    {
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new MemoryStream();
        var window = new byte[ReadWindowBytes];

        int read;
        while ((read = await content.ReadAsync(window, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                await buffer.DisposeAsync().ConfigureAwait(false);
                return Error.Validation(SourceExtractionErrorCodes.ExportTooLarge,
                    $"This {what}'s export is larger than the {maxBytes} byte limit.");
            }

            await buffer.WriteAsync(window.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return buffer;
    }
}

/// <summary>
/// What an access refusal — HTTP 401 or 403 — says about a source, which depends on how its host shares
/// what it serves. Every extractor names its own, rather than one answer for all: taken as "not public"
/// everywhere, a guide site's bot block marked every one of its guides due that night unavailable, each
/// left unchecked for a month, with nothing to stop the run asking the same site again.
/// </summary>
internal enum ForbiddenMeans
{
    /// <summary>
    /// The host shares each file on its own, as Google does: a refusal is about this one file, whose owner
    /// has stopped sharing it. Checked again rarely, and recovered once it is shared again.
    /// </summary>
    NotPublic,

    /// <summary>
    /// The host publishes everything it serves, as the guide sites and imgur do, so a refusal is the host
    /// turning the bot away — a firewall, a bot block, a region block — and says nothing about the source.
    /// Reported as <see cref="SourceExtractionErrorCodes.HostRefused"/>, so the source is left as it was
    /// and the rest of the run stops asking that host.
    /// </summary>
    HostRefused
}
