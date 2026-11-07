using System.Text.RegularExpressions;

namespace UseCases.UseCases.ClubChatForwarding;

/// <summary>Fills <see cref="Configuration.ClubChatForwardingConfiguration.Template"/>.</summary>
public static partial class ClubChatTemplate
{
    /// <summary>
    /// Replaces <c>{text}</c>, <c>{author}</c> and <c>{channel}</c> in one pass, so a placeholder
    /// written inside the message itself is left as it is. Unknown placeholders stay too.
    /// </summary>
    public static string Render(string template, string text, string author, string channel) =>
        Placeholder().Replace(template, match => match.Groups[1].Value switch
        {
            "text" => text,
            "author" => author,
            "channel" => channel,
            _ => match.Value,
        }).Trim();

    [GeneratedRegex(@"\{(text|author|channel)\}")]
    private static partial Regex Placeholder();
}
