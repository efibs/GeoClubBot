using Entities;
using FluentAssertions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.CountryChallenges.Configuration;

namespace GeoClubBot.Tests.TestBuilders;

/// <summary>
/// Builds country challenge files the way an admin writes them, and resolves them into plans, so tests
/// exercise the same inheritance rules production does instead of hand-building resolved records.
/// </summary>
public static class CountryChallengeFiles
{
    public const ulong ChannelId = 5000;

    /// <summary>A Monday; the weekdays after it follow in order.</summary>
    public static readonly DateOnly Monday = new(2026, 9, 28);

    public static readonly DateOnly Friday = Monday.AddDays(4);

    public static readonly DateOnly Sunday = Monday.AddDays(6);

    public static CountrySection Country(
        string name,
        string? code = null,
        string? mapId = null,
        GameSettingsSection? settings = null) =>
        new()
        {
            Name = name,
            Code = code,
            MapId = mapId ?? MapIdOf(name),
            Settings = settings
        };

    public static ChallengeSection Fixed(string name, DayOfWeek day, CountrySection country) =>
        new() { Name = name, Days = [day], Country = country };

    public static ChallengeSection Pool(string name, DayOfWeek day, params CountrySection[] countries) =>
        new() { Name = name, Days = [day], Pool = [.. countries] };

    public static CountryChallengesFile File(params ChallengeSection[] challenges) =>
        new() { ChannelId = ChannelId, Challenges = [.. challenges] };

    public static CountryChallengePlan Plan(CountryChallengesFile file)
    {
        var resolution = CountryChallengePlanResolver.Resolve(file);
        resolution.Errors.Should().BeEmpty("the test file is meant to be valid");
        return resolution.Plan!;
    }

    public static CountryChallengePlan Plan(params ChallengeSection[] challenges) => Plan(File(challenges));

    public static string MapIdOf(string country) => $"map-{country.ToLowerInvariant().Replace(' ', '-')}";

    public static CountryChallengePost Post(
        string challengeName,
        DateOnly date,
        string country = "Mongolia",
        string challengeId = "token-1",
        ulong channelId = ChannelId,
        DateOnly? resultsDueOn = null,
        string? countryCode = null) =>
        CountryChallengePost.Create(
            challengeName,
            date,
            country,
            countryCode,
            MapIdOf(country),
            mapName: null,
            timeLimit: 60,
            forbidMoving: true,
            forbidRotating: true,
            forbidZooming: true,
            challengeId,
            channelId,
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            resultsDueOn ?? date.AddDays(1));

    public static ChallengeResultHighscoresDto Highscores(params string[] userIds) => new()
    {
        Items = userIds.Select(id => new ChallengeResultItemDto
        {
            Game = new ChallengeResultGameDto
            {
                Player = new ChallengeResultPlayerDto
                {
                    Id = id,
                    Nick = $"nick-{id}",
                    TotalScore = new ChallengeResultPlayerScoreDto { Amount = "24000", Unit = "points" },
                    TotalDistance = new ChallengeResultPlayerDistanceDto
                    {
                        Meters = new ChallengeResultPlayerDistanceMetersDto { Amount = "120", Unit = "km" }
                    }
                }
            }
        }).ToList()
    };

    public static ClubChallengeResultPlayer Player(string userId, string? nickname = null) =>
        new(userId, nickname ?? $"nick-{userId}", "24000 points", "120km");
}
