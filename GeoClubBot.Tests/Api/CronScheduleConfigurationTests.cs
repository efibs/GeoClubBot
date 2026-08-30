using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Quartz;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// Every cron schedule in the settings must be a valid Quartz expression.
///
/// This is a start-up guard, not a style check: the job scanner parses these while the host is being
/// built, so one malformed expression stops the entire bot from starting — not just the feature it
/// belongs to. Quartz also rejects specifying both a day-of-month and a day-of-week, which reads as
/// perfectly reasonable cron to anyone used to other schedulers.
/// </summary>
public sealed class CronScheduleConfigurationTests
{
    [Fact]
    public void Every_configured_schedule_is_a_valid_quartz_expression()
    {
        var settings = FindSettingsFiles();
        settings.Should().NotBeEmpty("the settings file should be discoverable from the test run");

        var invalid = new List<string>();

        foreach (var path in settings)
        {
            foreach (var (key, expression) in ReadSchedules(path))
            {
                if (!CronExpression.IsValidExpression(expression))
                {
                    invalid.Add($"{Path.GetFileName(path)} → {key} = \"{expression}\"");
                }
            }
        }

        invalid.Should().BeEmpty("an invalid cron expression stops the host from starting");
    }

    /// <summary>Walks the settings tree for any key that names a schedule.</summary>
    private static IEnumerable<(string Key, string Expression)> ReadSchedules(string path)
    {
        using var document = JsonDocument.Parse(StripComments(File.ReadAllText(path)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var found = new List<(string, string)>();

        void Walk(JsonElement node, string trail)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in node.EnumerateObject())
            {
                var here = trail.Length == 0 ? property.Name : $"{trail}:{property.Name}";

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    Walk(property.Value, here);
                }
                else if (property.Value.ValueKind == JsonValueKind.String
                         && property.Name.Contains("Schedule", StringComparison.Ordinal)
                         && property.Value.GetString() is { Length: > 0 } expression)
                {
                    found.Add((here, expression));
                }
            }
        }

        Walk(document.RootElement, string.Empty);
        return found;
    }

    /// <summary>The settings files are JSON with comments, which the plain parser rejects.</summary>
    private static string StripComments(string json) =>
        Regex.Replace(Regex.Replace(json, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    /// <summary>
    /// Located by walking up from the test binary, so the test works from any working directory.
    /// The development file is gitignored and simply skipped when absent.
    /// </summary>
    private static List<string> FindSettingsFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoClubBot.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return [];
        }

        return [.. new[] { "appsettings.json", "appsettings.Development.json" }
            .Select(name => Path.Combine(directory.FullName, "GeoClubBot.API", name))
            .Where(File.Exists)];
    }
}
