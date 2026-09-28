using Configuration;
using FluentAssertions;
using Infrastructure.OutputAdapters.CountryChallenges;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.CountryChallenges;

/// <summary>
/// The country challenge file is written by hand. The source must accept what people naturally write —
/// comments, trailing commas, any casing, ids as strings — and refuse, with a location, anything it
/// would otherwise silently ignore.
/// </summary>
public sealed class JsonFileCountryChallengeConfigurationSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"country-challenges-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task ReadAsync_AcceptsCommentsTrailingCommasAnyCasingAndIdsAsStrings()
    {
        await File.WriteAllTextAsync(_path, """
            // The server's country challenges
            {
              "channelId": "1399071747075997878",
              "announcement": { "thread": { "enabled": true, "autoArchive": "oneWeek" }, },
              "challenges": [
                {
                  "Name": "Mongolia Monday",
                  "Days": [ "monday", "Friday" ],
                  "Dates": [ "2026-12-25" ],
                  "Country": { "Name": "Mongolia", "MapId": "abc" },
                },
              ],
            }
            """);

        var result = await ReadAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var file = result.Value;
        file.ChannelId.Should().Be(1399071747075997878UL);
        file.Announcement!.Thread!.AutoArchive.Should().Be(ThreadAutoArchive.OneWeek);
        file.Challenges!.Single().Days.Should().Equal(DayOfWeek.Monday, DayOfWeek.Friday);
        file.Challenges!.Single().Dates.Should().Equal(new DateOnly(2026, 12, 25));
    }

    [Fact]
    public async Task ReadAsync_RefusesAPropertyItDoesNotKnow_SayingWhereItIs()
    {
        await File.WriteAllTextAsync(_path, """
            {
              "ChannelId": 1,
              "Challenges": [
                { "Name": "Mongolia Monday", "Dayz": [ "Monday" ] }
              ]
            }
            """);

        var result = await ReadAsync();

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should()
            .StartWith("The country challenge file has a problem on line 4, at $.Challenges[0].Dayz:")
            .And.Contain("'Dayz'")
            .And.NotContain("UseCases.", "the .NET type name means nothing to whoever edits the file");
    }

    [Fact]
    public async Task ReadAsync_RefusesAWeekdayGivenAsANumber()
    {
        // 1 is Monday to some people and Sunday to others; only names are unambiguous.
        await File.WriteAllTextAsync(_path, """{ "Challenges": [ { "Days": [ 1 ] } ] }""");

        (await ReadAsync()).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_ReportsBrokenJson()
    {
        await File.WriteAllTextAsync(_path, """{ "ChannelId": 1, "Challenges": [ }""");

        (await ReadAsync()).Error.Message.Should().StartWith("The country challenge file has a problem on line 1");
    }

    [Fact]
    public async Task ReadAsync_ReportsAMissingFileWithItsFullPath()
    {
        var result = await ReadAsync();

        result.Error.Message.Should().Be($"The country challenge file was not found at '{_path}'.");
    }

    [Fact]
    public async Task ReadAsync_ReportsThatNoFileIsConfigured()
    {
        var result = await new JsonFileCountryChallengeConfigurationSource(Options(string.Empty)).ReadAsync();

        result.Error.Message.Should().Contain("CountryChallenges:ConfigurationFilePath");
    }

    /// <summary>
    /// The committed example is what people copy, so it has to load and pass validation exactly as it
    /// stands — otherwise the documentation itself would be the first broken configuration.
    /// </summary>
    [Fact]
    public async Task TheCommittedExampleFile_LoadsAndValidates()
    {
        var example = Path.Combine(SolutionRoot(), "CountryChallengesConfig.example.json");

        var result = await new JsonFileCountryChallengeConfigurationSource(Options(example)).ReadAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        var resolution = CountryChallengePlanResolver.Resolve(result.Value);
        resolution.Errors.Should().BeEmpty();
        resolution.Plan!.Warnings.Should().BeEmpty();
        resolution.Plan.Challenges.Select(c => c.Name).Should().Equal(
            "Mongolia Monday", "Argentina Friday", "Indonesia Friday", "Small Country Sunday");
    }

    private Task<Result<CountryChallengesFile>> ReadAsync() =>
        new JsonFileCountryChallengeConfigurationSource(Options(_path)).ReadAsync();

    private static IOptions<CountryChallengesConfiguration> Options(string path) =>
        Microsoft.Extensions.Options.Options.Create(new CountryChallengesConfiguration
        {
            Schedule = "0 0 17 ? * * *",
            ConfigurationFilePath = path
        });

    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the solution root should be discoverable from the test run");
        return directory!.FullName;
    }
}
