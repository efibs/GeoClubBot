using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QuartzExtensions;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// <see cref="ConfiguredCronJobAttribute.Config"/> is static and read while job types are scanned, so every
/// test that sets it runs alone rather than racing another test that sets something else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfiguredCronJobCollection
{
    public const string Name = "ConfiguredCronJob";
}

/// <summary>
/// A job can run its schedule in a time zone read from configuration. Jobs that do not ask for one must
/// stay on UTC, which is what every job ran in before.
/// </summary>
[Collection(ConfiguredCronJobCollection.Name)]
public sealed class ConfiguredCronJobAttributeTests
{
    private const string ScheduleKey = "Job:Schedule";
    private const string TimeZoneKey = "Job:TimeZone";

    [Fact]
    public void TimeZone_IsUtc_WhenTheJobAsksForNone()
    {
        UseConfiguration(timeZone: "Europe/Berlin");

        new ConfiguredCronJobAttribute(ScheduleKey).TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void TimeZone_IsUtc_WhenTheConfiguredZoneIsEmpty()
    {
        UseConfiguration(timeZone: "");

        new ConfiguredCronJobAttribute(ScheduleKey, TimeZoneKey).TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void TimeZone_IsTheConfiguredIanaZone()
    {
        UseConfiguration(timeZone: "Europe/Berlin");

        var attribute = new ConfiguredCronJobAttribute(ScheduleKey, TimeZoneKey);

        attribute.TimeZone.Id.Should().Be("Europe/Berlin");
        attribute.CronSchedule.Should().Be("0 0 17 ? * * *");
    }

    [Fact]
    public void TimeZone_RefusesAnUnknownZone_NamingTheKey()
    {
        UseConfiguration(timeZone: "Mars/Olympus_Mons");

        var create = () => new ConfiguredCronJobAttribute(ScheduleKey, TimeZoneKey);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Mars/Olympus_Mons'*'Job:TimeZone'*");
    }

    private static void UseConfiguration(string timeZone) =>
        ConfiguredCronJobAttribute.Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ScheduleKey] = "0 0 17 ? * * *",
                [TimeZoneKey] = timeZone
            })
            .Build();
}
