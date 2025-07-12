using System.Data;
using System.Net.Http.Json;
using Entities;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class HttpGeoGuessrAccess(IHttpClientFactory httpClientFactory) : IGeoGuessrAccess
{
    private const string GeoGuessrBaseUrl = "https://www.geoguessr.com/api";
    
    public async Task<List<GeoGuessrClubMember>> ReadClubMembersAsync(Guid clubId)
    {
        // Create the http client
        var client = httpClientFactory.CreateClient(nameof(HttpGeoGuessrAccess));
        
        // Make the http call
        var members =
            await client.GetFromJsonAsync<List<GeoGuessrClubMember>>($"{GeoGuessrBaseUrl}/v4/clubs/{clubId}/members")
            .ConfigureAwait(false);
        
        // If the call resulted in no members
        if (members == null || members.Count == 0)
        {
            throw new DataException("No club members found");
        }
        
        return members;
    }
}