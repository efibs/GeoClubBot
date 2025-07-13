using System.Data;
using System.Net.Http.Json;
using Entities;
using GeoClubBot;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class HttpGeoGuessrAccess(IHttpClientFactory httpClientFactory) : IGeoGuessrAccess
{
    public async Task<List<GeoGuessrClubMember>> ReadClubMembersAsync(Guid clubId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var members =
            await client.GetFromJsonAsync<List<GeoGuessrClubMember>>($"v4/clubs/{clubId}/members");

        // If the call resulted in no members
        if (members == null || members.Count == 0)
        {
            throw new DataException("No club members found");
        }

        return members;
    }

    public async Task<GeoGuessrClub> ReadClubAsync(Guid clubId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(HttpClientConstants.GeoGuessrHttpClientName);

        // Make the http call
        var club =
            await client.GetFromJsonAsync<GeoGuessrClub>($"v4/clubs/{clubId}");

        // If the call resulted in nothing
        if (club == null)
        {
            throw new DataException("Club not found");
        }

        return club;
    }
}