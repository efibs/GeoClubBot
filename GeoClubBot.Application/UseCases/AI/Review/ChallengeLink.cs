using System.Text.RegularExpressions;

namespace UseCases.UseCases.AI.Review;

/// <summary>
/// Reads a challenge token out of what a member pastes: a challenge link
/// (<c>https://www.geoguessr.com/challenge/AbCdEf123456</c>), a results link
/// (<c>…/results/AbCdEf123456</c>), or the token alone. Pure.
/// </summary>
public static partial class ChallengeLink
{
    /// <summary>The token, or null when the text holds none.</summary>
    public static string? TryReadToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().Trim('<', '>');

        var link = InLink().Match(trimmed);
        if (link.Success)
        {
            return link.Groups["token"].Value;
        }

        return BareToken().IsMatch(trimmed) ? trimmed : null;
    }

    [GeneratedRegex(@"geoguessr\.com/(?:[a-z]{2}/)?(?:challenge|results)/(?<token>[A-Za-z0-9]{8,64})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InLink();

    [GeneratedRegex(@"^[A-Za-z0-9]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex BareToken();
}
