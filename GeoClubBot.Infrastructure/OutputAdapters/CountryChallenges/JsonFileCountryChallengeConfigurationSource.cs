using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Configuration;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Utilities;

namespace Infrastructure.OutputAdapters.CountryChallenges;

/// <summary>
/// Reads the country challenge file from <see cref="CountryChallengesConfiguration.ConfigurationFilePath"/>.
///
/// The file is edited by hand, so the parser is forgiving about form — comments, trailing commas, any
/// casing, ids written as strings — and strict about content: a property it does not know is an error
/// naming where it is, because a typo'd setting would otherwise fall back to its default without anyone
/// noticing until the wrong message is posted.
/// </summary>
public sealed partial class JsonFileCountryChallengeConfigurationSource(IOptions<CountryChallengesConfiguration> options)
    : ICountryChallengeConfigurationSource
{
    private const string ErrorCode = "CountryChallenges.InvalidConfigurationFile";

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // Names only: an integer would be read as DayOfWeek too, and 1 meaning Monday is a guess away
        // from 1 meaning Sunday.
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public async Task<Result<CountryChallengesFile>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var path = options.Value.ConfigurationFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Error.Validation(ErrorCode,
                "No country challenge file is configured. Set CountryChallenges:ConfigurationFilePath.");
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Error.Validation(ErrorCode, $"The country challenge file was not found at '{Path.GetFullPath(path)}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error.Validation(ErrorCode, $"The country challenge file '{Path.GetFullPath(path)}' could not be read: {ex.Message}");
        }

        return Parse(json);
    }

    public static Result<CountryChallengesFile> Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CountryChallengesFile>(json, SerializerOptions);
            return file is null
                ? Error.Validation(ErrorCode, "The country challenge file is empty.")
                : file;
        }
        catch (JsonException ex)
        {
            return Error.Validation(ErrorCode, Describe(ex));
        }
    }

    /// <summary>
    /// Turns System.Text.Json's message into one an admin can act on: the line and property first, and
    /// without the .NET type names and byte offsets.
    /// </summary>
    private static string Describe(JsonException ex)
    {
        var message = ex.Message;

        var pathIndex = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (pathIndex > 0)
        {
            message = message[..pathIndex];
        }

        message = TypeNameRegex().Replace(message, string.Empty);

        var location = ex.LineNumber is { } line ? $"line {line + 1}" : "somewhere";
        var property = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? string.Empty : $", at {ex.Path}";

        return $"The country challenge file has a problem on {location}{property}: {message}";
    }

    [GeneratedRegex(@" contained in type '[^']*'")]
    private static partial Regex TypeNameRegex();
}
