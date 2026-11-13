using System.Text;

namespace UseCases.OutputPorts.GeoGuessr;

/// <summary>
/// GeoGuessr lists a round's Google panorama id hex-encoded, its ASCII bytes as upper-case hex
/// ("7470683379…" for "tph3y…", read from the highscores on 2026-11-13). Google's ids are 22
/// characters of letters, digits, '-' and '_', so a decoding that gives anything else was no encoding.
/// </summary>
public static class GeoGuessrPanoId
{
    /// <summary>The id Google knows the panorama by; <paramref name="panoId"/> itself when it is not hex, or null when empty.</summary>
    public static string? Decode(string? panoId)
    {
        if (string.IsNullOrWhiteSpace(panoId))
        {
            return null;
        }

        var trimmed = panoId.Trim();
        if (trimmed.Length % 2 != 0 || !trimmed.All(Uri.IsHexDigit))
        {
            return trimmed;
        }

        var decoded = Encoding.ASCII.GetString(Convert.FromHexString(trimmed));
        return decoded.Length > 0 && decoded.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? decoded : trimmed;
    }
}
