using System.Text.Json;

namespace GeoClubBot.ApiProbe;

/// <summary>
/// The probe's credentials and default target club, resolved from (in order) environment
/// variables and <c>appsettings.Local.json</c> next to the project. That file is covered by the
/// repository's <c>.gitignore</c> rule <c>appsettings.*.json</c>, so the token cannot be
/// committed by accident.
/// </summary>
public sealed record ProbeSettings(string NcfaToken, Guid? ClubId)
{
    private const string TokenEnvironmentVariable = "GEOGUESSR_NCFA_TOKEN";
    private const string ClubEnvironmentVariable = "GEOGUESSR_CLUB_ID";
    private const string LocalSettingsFileName = "appsettings.Local.json";

    /// <summary>
    /// Resolves the settings, or returns null after printing what the user needs to do. Never
    /// echoes the token itself.
    /// </summary>
    public static ProbeSettings? Resolve()
    {
        var (fileToken, fileClub) = ReadLocalSettingsFile();

        var token = FirstNonEmpty(Environment.GetEnvironmentVariable(TokenEnvironmentVariable), fileToken);
        var clubText = FirstNonEmpty(Environment.GetEnvironmentVariable(ClubEnvironmentVariable), fileClub);

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                $$"""
                  No GeoGuessr _ncfa token found.

                  Provide it in one of these two ways:

                    1. Environment variable:
                         export {{TokenEnvironmentVariable}}='<your _ncfa cookie value>'

                    2. {{LocalSettingsFileName}} beside the project (git-ignored):
                         {
                           "GeoGuessr": {
                             "NcfaToken": "<your _ncfa cookie value>",
                             "ClubId": "<your club guid>"
                           }
                         }

                  See {{Path.Combine(ProjectDirectory(), "README.md")}} for how to read the cookie
                  out of a logged-in browser session.
                  """);
            return null;
        }

        Guid? clubId = null;
        if (!string.IsNullOrWhiteSpace(clubText))
        {
            if (!Guid.TryParse(clubText, out var parsed))
            {
                Console.Error.WriteLine($"Configured club id '{clubText}' is not a valid GUID.");
                return null;
            }

            clubId = parsed;
        }

        return new ProbeSettings(token.Trim(), clubId);
    }

    private static (string? Token, string? ClubId) ReadLocalSettingsFile()
    {
        var path = Path.Combine(ProjectDirectory(), LocalSettingsFileName);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("GeoGuessr", out var section))
            {
                return (null, null);
            }

            return (ReadString(section, "NcfaToken"), ReadString(section, "ClubId"));
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"{LocalSettingsFileName} is not valid JSON: {ex.Message}");
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement section, string propertyName) =>
        section.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The project directory, so <c>dotnet run</c> from anywhere still finds the settings file
    /// (AppContext.BaseDirectory points at bin/&lt;config&gt;/net10.0).
    /// </summary>
    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("*.csproj").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;
}
