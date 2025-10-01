using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Constants;
using Entities;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.GeoGuessr.DTOs;

namespace Infrastructure.OutputAdapters;

public class HttpGeoGuessrAccess(IHttpClientFactory httpClientFactory) : IGeoGuessrAccess
{
    public async Task<List<GeoGuessrClubMemberDTO>> ReadClubMembersAsync(Guid clubId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var members =
            await client.GetFromJsonAsync<List<GeoGuessrClubMemberDTO>>($"v4/clubs/{clubId}/members").ConfigureAwait(false);

        // If the call resulted in no members
        if (members == null || members.Count == 0)
        {
            throw new DataException("No club members found");
        }

        return members;
    }

    public async Task<GeoGuessrClubDTO> ReadClubAsync(Guid clubId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var club =
            await client.GetFromJsonAsync<GeoGuessrClubDTO>($"v4/clubs/{clubId}").ConfigureAwait(false);

        // If the call resulted in nothing
        if (club == null)
        {
            throw new DataException("Club not found");
        }

        return club;
    }

    public async Task<GeoGuessrUserDTO?> ReadUserAsync(string userId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);
        
        // Make the http call
        var user =
            await client.GetFromJsonAsync<GeoGuessrUserDTO>($"v3/users/{userId}").ConfigureAwait(false);
        
        // If the call resulted in nothing
        if (user == null)
        {
            throw new DataException("User not found");
        }
        
        return user;
    }

    public async Task<GeoGuessrCreateChallengeResponseDTO> CreateChallengeAsync(
        GeoGuessrCreateChallengeRequestDTO request)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var response = await client.PostAsJsonAsync("v3/challenges", request).ConfigureAwait(false);

        // Get the response as a json string
        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return JsonSerializer.Deserialize<GeoGuessrCreateChallengeResponseDTO>(responseJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    public async Task<GeoGuessrChallengeResultHighscores?> ReadHighscoresAsync(string challengeId, int limit,
        int minRounds)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var results =
            await client.GetFromJsonAsync<GeoGuessrChallengeResultHighscores>(
                $"v3/results/highscores/{challengeId}?friends=false&limit={limit}&minrounds={minRounds}").ConfigureAwait(false);

        return results;
    }
}