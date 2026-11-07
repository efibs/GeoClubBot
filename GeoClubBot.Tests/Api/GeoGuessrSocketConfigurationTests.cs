using System.ComponentModel.DataAnnotations;
using Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoClubBot.Tests.Api;

// In Api/ rather than a Configuration/ folder: a GeoClubBot.Tests.Configuration namespace would
// shadow the Configuration project's namespace for the whole test assembly.
public sealed class GeoGuessrSocketConfigurationTests
{
    [Fact]
    public void Defaults_AreValid() =>
        Validate(new GeoGuessrSocketConfiguration()).Should().BeEmpty();

    [Fact]
    public void SettingsFile_RepeatsTheDefaults() =>
        ShippedSettings().Should().BeEquivalentTo(new GeoGuessrSocketConfiguration());

    [Fact]
    public void SettingsFile_HasNoKeyNamedLikeASchedule()
    {
        // Start-up parses every *Schedule* key as a cron, and the E2E host rewrites them to "never".
        ShippedSection().AsEnumerable().Select(kv => kv.Key)
            .Should().NotContain(key => key.Contains("Schedule", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://api.geoguessr.com/ws")]
    [InlineData("api.geoguessr.com/ws")]
    public void Validate_RefusesAUrlThatIsNoWebSocketAddress(string url) =>
        Validate(new GeoGuessrSocketConfiguration { Url = url }).Should().Equal(nameof(GeoGuessrSocketConfiguration.Url));

    [Fact]
    public void Validate_AcceptsAPlainWsUrl_ForALocalServer() =>
        Validate(new GeoGuessrSocketConfiguration { Url = "ws://127.0.0.1:5000/ws" }).Should().BeEmpty();

    [Theory]
    [InlineData(nameof(GeoGuessrSocketConfiguration.HeartbeatInterval))]
    [InlineData(nameof(GeoGuessrSocketConfiguration.KeepAlive))]
    [InlineData(nameof(GeoGuessrSocketConfiguration.PollWhileDisconnected))]
    [InlineData(nameof(GeoGuessrSocketConfiguration.AuthRetry))]
    [InlineData(nameof(GeoGuessrSocketConfiguration.AlertAfter))]
    public void Validate_RefusesADurationThatIsNotPositive(string property)
    {
        var settings = new GeoGuessrSocketConfiguration();
        typeof(GeoGuessrSocketConfiguration).GetProperty(property)!.SetValue(settings, TimeSpan.Zero);

        Validate(settings).Should().Equal(property);
    }

    [Fact]
    public void Validate_AllowsNoBatchWindow_ButNotANegativeOne()
    {
        Validate(new GeoGuessrSocketConfiguration { BatchWindow = TimeSpan.Zero }).Should().BeEmpty();
        Validate(new GeoGuessrSocketConfiguration { BatchWindow = TimeSpan.FromSeconds(-1) })
            .Should().Equal(nameof(GeoGuessrSocketConfiguration.BatchWindow));
    }

    [Fact]
    public void Validate_RefusesAMaximumBelowItsStart()
    {
        Validate(new GeoGuessrSocketConfiguration { SupersededYieldMax = TimeSpan.FromMinutes(5) })
            .Should().Equal(nameof(GeoGuessrSocketConfiguration.SupersededYieldMax));
        Validate(new GeoGuessrSocketConfiguration { BlockedRetryMax = TimeSpan.FromMinutes(5) })
            .Should().Equal(nameof(GeoGuessrSocketConfiguration.BlockedRetryMax));
    }

    [Fact]
    public void AClub_UsesTheSocket_UnlessItOrTheWholeFeatureIsOff()
    {
        var on = new GeoGuessrSocketConfiguration();
        var off = new GeoGuessrSocketConfiguration { Enabled = false };

        Club(useSocket: null).GetUseSocket(on).Should().BeTrue();
        Club(useSocket: false).GetUseSocket(on).Should().BeFalse();
        Club(useSocket: true).GetUseSocket(off).Should().BeFalse();
    }

    [Fact]
    public void AClub_MayPollOnItsOwnInterval()
    {
        var defaults = new GeoGuessrSocketConfiguration();

        Club().GetSocketPollWhileDisconnected(defaults).Should().Be(TimeSpan.FromHours(1));
        new GeoGuessrClubEntry { ClubId = Guid.NewGuid(), NcfaToken = "x", SocketPollWhileDisconnected = TimeSpan.FromHours(2) }
            .GetSocketPollWhileDisconnected(defaults).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Options_BindTheSection_AndFailAtStartUpWhenInvalid()
    {
        var services = new ServiceCollection();
        services.AddClubBotOptions(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GeoGuessrSocket:Url"] = "https://example.com" })
            .Build());

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<GeoGuessrSocketConfiguration>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Url*");
    }

    private static GeoGuessrClubEntry Club(bool? useSocket = null) =>
        new() { ClubId = Guid.NewGuid(), NcfaToken = "x", UseSocket = useSocket };

    private static IConfigurationSection ShippedSection()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the solution root should be discoverable from the test run");

        var section = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory!.FullName, "GeoClubBot.API", "appsettings.json"))
            .Build()
            .GetSection(GeoGuessrSocketConfiguration.SectionName);
        section.Exists().Should().BeTrue();
        return section;
    }

    private static GeoGuessrSocketConfiguration ShippedSettings() =>
        ShippedSection().Get<GeoGuessrSocketConfiguration>()!;

    private static IEnumerable<string> Validate(GeoGuessrSocketConfiguration settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        return results.SelectMany(result => result.MemberNames.Any() ? result.MemberNames : [result.ErrorMessage ?? string.Empty]);
    }
}
