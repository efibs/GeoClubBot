using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GeoClubBot.MockGeoGuessr.Endpoints;

public static class MockGeoGuessrEndpoints
{
    public static void MapMockGeoGuessrEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Serve the mock UI HTML
        endpoints.MapGet("/mock", () =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream("GeoClubBot.MockGeoGuessr.wwwroot.mock.html");
            if (stream is null)
                return Results.NotFound("mock.html not found");
            return Results.Stream(stream, "text/html");
        }).ExcludeFromDescription();
    }
}

// Request DTOs used by MockManagementController
public record UpdateClubRequest(string? Name = null, int? Level = null, int? Xp = null, int? MaxMemberCount = null, string? Tag = null, string? Description = null);
public record AddMemberRequest(string UserId);
public record MoveMemberRequest(Guid TargetClubId);
public record AddXpRequest(int Amount = 20);
public record UpdateMemberRequest(int? Xp = null, int? WeeklyXp = null, int? Role = null);
public record CreateUserRequest(string Nick, string? UserId = null, string? CountryCode = "us", bool IsProUser = true);
public record UpdateUserRequest(string? Nick = null, string? CountryCode = null, bool? IsProUser = null, int? Elo = null, int? Rating = null);
public record CreateChallengeRequest(string? Map = "world", int TimeLimit = 60, bool ForbidMoving = false, bool ForbidRotating = false, bool ForbidZooming = false);
public record AddScoreRequest(string UserId, int Score = 25000, int Distance = 100);
public record AddActivityRequest(string UserId, int XpReward = 20);
public record AddMissionRequest(
    string Type,
    string GameMode,
    int TargetProgress,
    int CurrentProgress = 0,
    bool Completed = false,
    DateTimeOffset? EndDate = null,
    int RewardAmount = 100,
    string RewardType = "Coins");
public record UpdateNextMissionDateRequest(DateTimeOffset NextMissionDate);
