using UseCases.UseCases.CountryChallenges.Configuration;

namespace UseCases.UseCases.CountryChallenges.Rendering;

/// <summary>How the GeoGuessr settings of a challenge read in a message: "NMPZ · 1 min".</summary>
public static class ChallengeSettingsText
{
    public static string Describe(GameSettings settings) => $"{Mode(settings)} · {TimeLimit(settings.TimeLimit)}";

    /// <summary>The movement restrictions, named the way players name them.</summary>
    public static string Mode(GameSettings settings)
    {
        return (settings.ForbidMoving, settings.ForbidRotating, settings.ForbidZooming) switch
        {
            (true, true, true) => "NMPZ",
            (false, false, false) => "Moving",
            (true, false, false) => "No move",
            _ => Capitalize(string.Join(", ", Restrictions(settings)))
        };
    }

    public static string TimeLimit(int seconds)
    {
        if (seconds <= 0)
        {
            return "no time limit";
        }

        if (seconds < 60)
        {
            return $"{seconds} s";
        }

        return seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60} min {seconds % 60} s";
    }

    private static IEnumerable<string> Restrictions(GameSettings settings)
    {
        if (settings.ForbidMoving)
        {
            yield return "no move";
        }

        if (settings.ForbidRotating)
        {
            yield return "no pan";
        }

        if (settings.ForbidZooming)
        {
            yield return "no zoom";
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
