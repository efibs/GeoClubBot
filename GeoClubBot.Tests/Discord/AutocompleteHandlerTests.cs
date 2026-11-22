using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using UseCases.OutputPorts.Projections;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Unit tests for the pure filter/map logic of the autocomplete handlers. These target the
/// <c>BuildSuggestions</c> helpers directly (exposed via <c>InternalsVisibleTo</c>) so no
/// Discord interaction context needs to be mocked.
/// </summary>
public sealed class AutocompleteHandlerTests
{
    // ---- User handlers (shared UserSuggestions.Build) ----

    private static UserNameProjection User(string nickname, string? userId = null) =>
        new(userId ?? $"id-{nickname}", nickname);

    [Fact]
    public void User_FiltersBySubstringCaseInsensitively()
    {
        IReadOnlyList<UserNameProjection> users = [User("Alice"), User("Bob"), User("Alibaba"), User("Carol")];

        var results = UserSuggestions.Build(users, "ali").ToList();

        results.Select(r => r.Name).Should().BeEquivalentTo("Alice", "Alibaba");
    }

    [Fact]
    public void User_ShowsTheNickname_ButPassesTheUserId()
    {
        IReadOnlyList<UserNameProjection> users = [User("Alice", "5f1a")];

        var result = UserSuggestions.Build(users, string.Empty).Single();

        result.Name.Should().Be("Alice");
        result.Value.Should().Be("5f1a");
    }

    [Fact]
    public void User_PlayersSharingANickname_AreToldApartByTheirId()
    {
        IReadOnlyList<UserNameProjection> users = [User("Alice", "a1"), User("alice", "a2"), User("Bob", "b1")];

        var results = UserSuggestions.Build(users, string.Empty).ToList();

        results.Select(r => (r.Name, r.Value)).Should().Equal(
            ("Alice (a1)", (object)"a1"), ("alice (a2)", "a2"), ("Bob", "b1"));
    }

    [Fact]
    public void User_EmptyInputReturnsAll()
    {
        IReadOnlyList<UserNameProjection> users = [User("Alice"), User("Bob"), User("Carol")];

        var results = UserSuggestions.Build(users, string.Empty).ToList();

        results.Should().HaveCount(3);
    }

    [Fact]
    public void User_CapsAtTwentyFive()
    {
        var users = Enumerable.Range(0, 30).Select(i => User($"Player{i:00}")).ToList();

        var results = UserSuggestions.Build(users, string.Empty).ToList();

        results.Should().HaveCount(25);
    }

    // ---- Club handler ----

    [Fact]
    public void Club_ShowsTheName_ButPassesTheId()
    {
        var club = Club.Create(Guid.NewGuid(), "Speedrunners", 10);

        var result = ClubAutocompleteHandler.BuildSuggestions([club], string.Empty).Single();

        result.Name.Should().Be("Speedrunners");
        result.Value.Should().Be(club.ClubId.ToString());
    }

    [Fact]
    public void Club_FiltersBySubstringCaseInsensitively()
    {
        var clubs = new List<Club>
        {
            Club.Create(Guid.NewGuid(), "Speedrunners", 10),
            Club.Create(Guid.NewGuid(), "Casuals", 5),
            Club.Create(Guid.NewGuid(), "Speed Demons", 8),
        };

        var results = ClubAutocompleteHandler.BuildSuggestions(clubs, "speed").ToList();

        results.Select(r => r.Name).Should().BeEquivalentTo("Speedrunners", "Speed Demons");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ClubOption_LeftOut_IsTheMainClub(string? value)
    {
        ClubOption.TryParse(value, out var clubId).Should().BeTrue();
        clubId.Should().BeNull();
    }

    [Fact]
    public void ClubOption_APickedClub_IsItsId()
    {
        var id = Guid.NewGuid();

        ClubOption.TryParse(id.ToString(), out var clubId).Should().BeTrue();
        clubId.Should().Be(id);
    }

    [Fact]
    public void ClubOption_ATypedName_IsRejected()
    {
        // Discord sends what was typed when no suggestion is picked.
        ClubOption.TryParse("Speedrunners", out _).Should().BeFalse();
    }

    // ---- Timezone handler ----

    [Fact]
    public void Timezone_FiltersByIdAndUsesIdAsValue()
    {
        var timezones = new List<TimeZoneInfo>
        {
            TimeZoneInfo.CreateCustomTimeZone("Europe/Berlin", TimeSpan.FromHours(1), "Berlin", "Berlin"),
            TimeZoneInfo.CreateCustomTimeZone("Europe/Paris", TimeSpan.FromHours(1), "Paris", "Paris"),
            TimeZoneInfo.CreateCustomTimeZone("America/New_York", TimeSpan.FromHours(-5), "New York", "New York"),
        };

        var results = TimezoneAutocompleteHandler.BuildSuggestions(timezones, "europe").ToList();

        results.Select(r => r.Name).Should().BeEquivalentTo("Europe/Berlin", "Europe/Paris");
        results.Should().OnlyContain(r => Equals(r.Value, r.Name));
    }

    [Fact]
    public void Timezone_CapsAtTwentyFive()
    {
        var timezones = Enumerable.Range(0, 30)
            .Select(i => TimeZoneInfo.CreateCustomTimeZone($"Zone/{i:00}", TimeSpan.Zero, $"Zone {i}", $"Zone {i}"))
            .ToList();

        var results = TimezoneAutocompleteHandler.BuildSuggestions(timezones, "Zone").ToList();

        results.Should().HaveCount(25);
    }

    // ---- StrikeId handler ----

    [Fact]
    public void StrikeId_RendersNicknameAndDateLabelWithIdValue()
    {
        var id = Guid.NewGuid();
        var option = new StrikeIdAutocompleteHandler.StrikeOption(id, "Alice", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var result = StrikeIdAutocompleteHandler.BuildSuggestions([option], string.Empty).Single();

        result.Name.Should().Be("Alice — 2026-06-01");
        result.Value.Should().Be(id.ToString());
    }

    [Fact]
    public void StrikeId_OrdersByNicknameThenTimestamp()
    {
        var options = new List<StrikeIdAutocompleteHandler.StrikeOption>
        {
            new(Guid.NewGuid(), "Bob", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            new(Guid.NewGuid(), "Alice", new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero)),
            new(Guid.NewGuid(), "Alice", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var results = StrikeIdAutocompleteHandler.BuildSuggestions(options, string.Empty).ToList();

        results.Select(r => r.Name).Should().ContainInOrder(
            "Alice — 2026-06-01", "Alice — 2026-06-05", "Bob — 2026-06-01");
    }

    [Fact]
    public void StrikeId_FiltersByNicknameFragment()
    {
        var options = new List<StrikeIdAutocompleteHandler.StrikeOption>
        {
            new(Guid.NewGuid(), "Alice", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            new(Guid.NewGuid(), "Bob", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        var results = StrikeIdAutocompleteHandler.BuildSuggestions(options, "bob").ToList();

        results.Should().ContainSingle().Which.Name.Should().Be("Bob — 2026-06-01");
    }

    // ---- ExcuseId handler ----

    [Fact]
    public void ExcuseId_RendersNicknameAndRangeLabelWithIdValue()
    {
        var id = Guid.NewGuid();
        var option = new ExcuseIdAutocompleteHandler.ExcuseOption(
            id,
            "Alice",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero));

        var result = ExcuseIdAutocompleteHandler.BuildSuggestions([option], string.Empty).Single();

        result.Name.Should().Be("Alice — 2026-06-01→2026-06-07");
        result.Value.Should().Be(id.ToString());
    }

    [Fact]
    public void ExcuseId_FiltersByNicknameFragment()
    {
        var options = new List<ExcuseIdAutocompleteHandler.ExcuseOption>
        {
            new(Guid.NewGuid(), "Alice", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero)),
            new(Guid.NewGuid(), "Bob", new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero)),
        };

        var results = ExcuseIdAutocompleteHandler.BuildSuggestions(options, "ali").ToList();

        results.Should().ContainSingle().Which.Value.Should().Be(options[0].ExcuseId.ToString());
    }
}
