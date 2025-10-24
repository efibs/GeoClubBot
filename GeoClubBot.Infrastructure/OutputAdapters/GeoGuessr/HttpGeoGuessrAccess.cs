using System.Data;
using System.Net.Http.Json;
using Entities;
using Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetChallengeResults;
using Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClub;
using Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetClubMembers;
using Infrastructure.OutputAdapters.GeoGuessr.DTOs.PostChallenge;
using UseCases.OutputPorts.GeoGuessr;
using UserDto = Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetUser.UserDto;
using UserDtoAssembler = Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetUser.UserDtoAssembler;

namespace Infrastructure.OutputAdapters.GeoGuessr;

public class HttpGeoGuessrAccess(HttpClient client) : IGeoGuessrAccess
{
    public async Task<List<ClubMember>> ReadClubMembersAsync(Guid clubId)
    {
        // Make the http call
        var members =
            await client.GetFromJsonAsync<List<ClubMemberDto>>($"v4/clubs/{clubId}/members").ConfigureAwait(false);

        // If the call resulted in no members
        if (members == null || members.Count == 0)
        {
            throw new DataException("No club members found");
        }
        
        // Assemble entities
        var entities = ClubMemberDtoAssembler.AssembleEntities(members, clubId);

        return entities;
    }

    public async Task<Club> ReadClubAsync(Guid clubId)
    {
        // Make the http call
        var club =
            await client.GetFromJsonAsync<ClubDto>($"v4/clubs/{clubId}").ConfigureAwait(false);

        // If the call resulted in nothing
        if (club == null)
        {
            throw new DataException("Club not found");
        }

        // Assemble the entity
        var entity = ClubDtoAssembler.AssembleEntity(club);
        
        return entity;
    }

    public async Task<GeoGuessrUser?> ReadUserAsync(string userId)
    {
        // Make the http call
        var user =
            await client.GetFromJsonAsync<UserDto>($"v3/users/{userId}").ConfigureAwait(false);
        
        // If the call resulted in nothing
        if (user == null)
        {
            throw new DataException("User not found");
        }
        
        // Assemble the entity
        var entity = UserDtoAssembler.AssembleEntity(user);
        
        return entity;
    }

    public async Task<string?> CreateChallengeAsync(int accessLevel, 
        int challengeType, 
        bool forbidMoving, 
        bool forbidRotating, 
        bool forbidZooming, 
        string map,
        int timeLimit)
    {
        // Create the request
        var request = new PostChallengeRequestDto(accessLevel, challengeType, forbidMoving, forbidRotating, forbidZooming, map, timeLimit);
        
        // Make the http call
        var response = await client.PostAsJsonAsync("v3/challenges", request).ConfigureAwait(false);

        // Get the response as a json string
        var responseObject = await response.Content.ReadFromJsonAsync<PostChallengeResponseDto>().ConfigureAwait(false);

        return responseObject?.Token;
    }

    public async Task<List<ClubChallengeResultPlayer>?> ReadHighscoresAsync(string challengeId, int limit,
        int minRounds)
    {
        // Make the http call
        var results =
            await client.GetFromJsonAsync<ChallengeResultHighscoresDto>(
                $"v3/results/highscores/{challengeId}?friends=false&limit={limit}&minrounds={minRounds}").ConfigureAwait(false);

        // If there are no results
        if (results == null)
        {
            return null;
        }
        
        // Assemble the entities
        var entities = ChallengeResultHighScoresAssembler.AssembleEntities(results);
        
        return entities;
    }
}