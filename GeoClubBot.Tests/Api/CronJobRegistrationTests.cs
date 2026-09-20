using System.Reflection;
using FluentAssertions;
using GeoClubBot.DependencyInjection.Modules;
using Infrastructure.InputAdapters.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using QuartzExtensions;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Every job carrying a <see cref="CronJobAttribute"/> must end up in the scheduler with a cron
/// trigger attached.
///
/// The scanner in <c>AddCronJobs</c> registers job and trigger separately and links them by
/// <see cref="JobKey"/>; nothing fails loudly if that link breaks — the host starts happily and the
/// job simply never fires, which is invisible until someone notices a missing daily message. That
/// registration is also exactly what a Quartz major upgrade rewrites.
/// </summary>
public sealed class CronJobRegistrationTests
{
    [Fact]
    public async Task Every_cron_job_is_scheduled_with_its_configured_expression()
    {
        ConfiguredCronJobAttribute.Config = LoadApiSettings();

        var services = new ServiceCollection();
        services.AddQuartzModule();

        await using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var expected = typeof(IJobAssemblyMarker).Assembly.GetTypes()
            .Where(t => typeof(IJob).IsAssignableFrom(t))
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<CronJobAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToList();

        expected.Should().NotBeEmpty("the jobs assembly should contain cron jobs to schedule");

        foreach (var (type, attribute) in expected)
        {
            var jobKey = new JobKey(type.Name);
            (await scheduler.Exists(jobKey)).Should().BeTrue($"{type.Name} should be registered");

            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            triggers.Should().ContainSingle($"{type.Name} should have exactly one trigger")
                .Which.Should().BeAssignableTo<ICronTrigger>()
                .Which.CronExpressionString.Should().Be(attribute!.CronSchedule);
        }
    }

    /// <summary>
    /// The attribute reads its schedule out of configuration while the job types are being scanned,
    /// so the real settings file has to be loaded first — located by walking up from the test binary.
    /// </summary>
    private static IConfiguration LoadApiSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the solution root should be discoverable from the test run");

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory!.FullName, "GeoClubBot.API", "appsettings.json"))
            .Build();
    }
}
