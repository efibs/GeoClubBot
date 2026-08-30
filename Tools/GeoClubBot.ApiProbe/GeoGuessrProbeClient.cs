using System.Net;
using System.Text.Json;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// A minimal, GET-only GeoGuessr API client. Hand-rolled rather than reusing the solution's Refit
/// client so that responses arrive as raw JSON, with every field intact.
/// </summary>
public sealed class GeoGuessrProbeClient : IDisposable
{
    /// <summary>Same base address the bot uses (GeoClubBot.API/DependencyInjection/ClubBotServices.cs).</summary>
    private const string DefaultBaseAddress = "https://www.geoguessr.com/api";

    /// <summary>
    /// Overrides the target host. Set it to point the probe at a stand-in (the solution's
    /// MockGeoGuessr, or a throwaway server while working on the probe itself) instead of the
    /// live API.
    /// </summary>
    private const string BaseAddressEnvironmentVariable = "GEOGUESSR_API_BASE_URL";

    private readonly HttpClient _httpClient;
    private readonly string _baseAddress;

    public GeoGuessrProbeClient(string ncfaToken)
    {
        _baseAddress = (Environment.GetEnvironmentVariable(BaseAddressEnvironmentVariable) ?? DefaultBaseAddress)
            .TrimEnd('/');

        // The guard wraps the real handler, so nothing can leave this client except GET/HEAD.
        var handler = new ReadOnlyGuardHandler(new HttpClientHandler { AllowAutoRedirect = true });

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseAddress + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Matches how the bot authenticates: a raw Cookie header, not a CookieContainer.
        _httpClient.DefaultRequestHeaders.Add("Cookie", $"_ncfa={ncfaToken}");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GeoClubBot.ApiProbe (read-only diagnostics)");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Issues one GET against <paramref name="relativePath"/> (with or without a leading slash) and
    /// returns the parsed body. Throws <see cref="ProbeRequestException"/> on a non-success status.
    /// </summary>
    public async Task<JsonDocument> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = relativePath.TrimStart('/');
        Console.WriteLine($"GET {_baseAddress}/{path}");

        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProbeRequestException(response.StatusCode, TokenRedactor.Redact(body));
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ProbeRequestException(
                response.StatusCode,
                $"Response was not JSON ({ex.Message}). First 500 characters:\n{TokenRedactor.Redact(body[..Math.Min(500, body.Length)])}");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class ProbeRequestException(HttpStatusCode statusCode, string body)
    : Exception($"GeoGuessr returned {(int)statusCode} {statusCode}.\n{body}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
