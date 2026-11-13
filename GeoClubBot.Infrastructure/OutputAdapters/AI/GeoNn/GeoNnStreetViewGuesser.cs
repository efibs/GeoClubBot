using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;

namespace Infrastructure.OutputAdapters.AI.GeoNn;

/// <summary>
/// Asks GeoNN (the maintainer's GeoGuessr model, served by its <c>scripts/play.py</c>) where a panorama
/// is: <c>POST /api/predict</c> with the panorama id and how it was shown — <c>nm</c> for the whole
/// panorama, <c>nmpz</c> for one view at its heading, pitch and zoom. GeoNN fetches the panorama from
/// Google itself and answers its guess and the countries it weighs (<c>{"code","prob"}</c>, likeliest
/// first; read 2026-11-13). One attempt, and never a failure: the review goes on without the reading.
/// </summary>
public sealed partial class GeoNnStreetViewGuesser(
    IHttpClientFactory httpClientFactory,
    IOptions<AiRoundReviewConfiguration> configuration,
    ILogger<GeoNnStreetViewGuesser> logger) : IStreetViewGuesser
{
    public const string HttpClientName = "GeoNN";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsAvailable => configuration.Value.StreetViewGuesser.IsConfigured;

    public async Task<PanoramaGuess?> GuessAsync(PanoramaView view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        var settings = configuration.Value.StreetViewGuesser;
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(view.PanoId))
        {
            return null;
        }

        var fixedView = view.Mode == PanoramaViewMode.FixedView;
        var request = new PredictRequest(
            view.PanoId,
            fixedView ? "nmpz" : "nm",
            fixedView ? view.Heading : null,
            fixedView ? view.Pitch ?? 0 : 0,
            fixedView ? view.Zoom ?? 0 : 0,
            string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client
                .PostAsJsonAsync(new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), "api/predict"), request, Json, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, (int)response.StatusCode, view.PanoId);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<PredictResponse>(Json, timeout.Token).ConfigureAwait(false);
            if (body is not { Lat: { } lat, Lng: { } lng })
            {
                return null;
            }

            IReadOnlyList<CountryProbability> countries =
            [
                .. (body.Countries ?? [])
                    .Where(country => !string.IsNullOrWhiteSpace(country.Code) && country.Prob is >= 0)
                    .Select(country => new CountryProbability(country.Code!.Trim().ToUpperInvariant(), Math.Clamp(country.Prob!.Value, 0, 1)))
                    .OrderByDescending(country => country.Probability)
            ];

            return new PanoramaGuess(lat, lng, countries, body.Model ?? settings.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, view.PanoId);
            return null;
        }
    }

    private sealed record PredictRequest(
        [property: JsonPropertyName("pano_id")] string PanoId,
        string Mode,
        double? Heading,
        double Pitch,
        double Zoom,
        string? Model);

    private sealed record PredictResponse(string? Model, double? Lat, double? Lng, List<PredictCountry>? Countries);

    private sealed record PredictCountry(string? Code, double? Prob);

    [LoggerMessage(LogLevel.Warning, "GeoNN answered {Status} for panorama {PanoId}; the round is reviewed without its reading.")]
    static partial void LogRefused(ILogger logger, int status, string panoId);

    [LoggerMessage(LogLevel.Warning, "GeoNN could not be asked about panorama {PanoId}; the round is reviewed without its reading.")]
    static partial void LogFailed(ILogger logger, Exception exception, string panoId);
}
