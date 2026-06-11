using Entities;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.Autocomplete;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Unit tests for the pure filter/map logic of the autocomplete handlers. These target the
/// <c>BuildSuggestions</c> helpers directly (exposed via <c>InternalsVisibleTo</c>) so no
/// Discord interaction context needs to be mocked.
/// </summary>
public sealed class AutocompleteHandlerTests
{
    // ---- Nickname handlers (shared NicknameSuggestions.Build) ----

    [Fact]
    public void Nickname_FiltersBySubstringCaseInsensitively()
    {
        IReadOnlyList<string> nicknames = ["Alice", "Bob", "Alibaba", "Carol"];

        var results = NicknameSuggestions.Build(nicknames, "ali").ToList();

        results.Select(r => r.Name).Should().BeEquivalentTo("Alice", "Alibaba");
    }

    [Fact]
    public void Nickname_ValueEqualsLabel()
    {
        IReadOnlyList<string> nicknames = ["Alice"];

        var result = NicknameSuggestions.Build(nicknames, string.Empty).Single();

        result.Name.Should().Be("Alice");
        result.Value.Should().Be("Alice");
    }

    [Fact]
    public void Nickname_EmptyInputReturnsAll()
    {
        IReadOnlyList<string> nicknames = ["Alice", "Bob", "Carol"];

        var results = NicknameSuggestions.Build(nicknames, string.Empty).ToList();

        results.Should().HaveCount(3);
    }

    [Fact]
    public void Nickname_CapsAtTwentyFive()
    {
        var nicknames = Enumerable.Range(0, 30).Select(i => $"Player{i:00}").ToList();

        var results = NicknameSuggestions.Build(nicknames, string.Empty).ToList();

        results.Should().HaveCount(25);
    }

    // ---- ClubName handler ----

    [Fact]
    public void ClubName_ValueIsTheNameNotTheId()
    {
        var club = Club.Create(Guid.NewGuid(), "Speedrunners", 10);

        var result = ClubNameAutocompleteHandler.BuildSuggestions([club], string.Empty).Single();

        result.Name.Should().Be("Speedrunners");
        result.Value.Should().Be("Speedrunners");
    }

    [Fact]
    public void ClubName_FiltersBySubstringCaseInsensitively()
    {
        var clubs = new List<Club>
        {
            Club.Create(Guid.NewGuid(), "Speedrunners", 10),
            Club.Create(Guid.NewGuid(), "Casuals", 5),
            Club.Create(Guid.NewGuid(), "Speed Demons", 8),
        };

        var results = ClubNameAutocompleteHandler.BuildSuggestions(clubs, "speed").ToList();

        results.Select(r => r.Name).Should().BeEquivalentTo("Speedrunners", "Speed Demons");
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
